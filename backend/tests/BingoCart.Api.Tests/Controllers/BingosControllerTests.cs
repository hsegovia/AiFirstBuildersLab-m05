using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using BingoCart.Application.Bingos.Dtos;
using BingoCart.Domain.Bingos;
using BingoCart.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace BingoCart.Api.Tests.Controllers;

/// <summary>
/// Tests de integración del endpoint <c>POST /api/bingos</c> (Block 4 del spec FEAT-003) contra el
/// stack completo levantado en memoria (<see cref="WebApplicationFactory{T}"/>) y el SQL Server
/// real dockerizado (puerto 14330). Mismo patrón que <c>OrganizadoresControllerTests</c>: cada test
/// crea su propio organizador (mail único, Rule #0 de testing.instructions.md) y elimina el
/// organizador y cualquier bingo/cartón creado en <see cref="DisposeAsync"/>. La cookie
/// <c>bingocart_auth</c> es <c>Secure</c>, así que todo request autenticado usa un cliente HTTPS
/// dedicado (<c>BaseAddress = https://localhost</c>), mismo mecanismo que
/// <c>Perfil_ConCookieDeLoginReal_...</c>.
/// </summary>
public sealed class BingosControllerTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Server=localhost,14330;Database=BingoCart;User Id=sa;" +
        "Password=BingoCart_Dev2026!;TrustServerCertificate=True;Encrypt=True;";

    private const string TelefonoValido = "+54 11 4444-5555";
    private const string PasswordValida = "Passw0rd!";

    // Multiplicadores del algoritmo estándar CUIT/CUIL (mismo que CuitValidator, Domain). Cada
    // test genera su propio CUIT válido (en vez de reutilizar una constante fija como
    // OrganizadoresControllerTests) porque AspNetUsers.Cuit tiene un índice único (Block 1 de
    // FEAT-001a): con un CUIT hardcodeado compartido, ejecutar esta clase en paralelo con
    // OrganizadoresControllerTests (collections distintas, xUnit las corre en paralelo por
    // default) produce colisiones intermitentes del índice único y un 500 no relacionado con lo
    // que el test intenta validar — no es un problema de "Regla #0" de datos no limpiados, sino de
    // dos suites generando la MISMA fila válida al mismo tiempo.
    private static readonly int[] MultiplicadoresCuit = { 5, 4, 3, 2, 7, 6, 5, 4, 3, 2 };
    private static long _secuenciaCuit;

    private static string NuevoCuitValido()
    {
        while (true)
        {
            var secuencia = Interlocked.Increment(ref _secuenciaCuit);
            var cuerpo = "30" + (10_000_000 + secuencia % 89_999_999).ToString("D8");
            var digitos = cuerpo.Select(c => c - '0').ToArray();

            var suma = 0;
            for (var i = 0; i < MultiplicadoresCuit.Length; i++)
            {
                suma += digitos[i] * MultiplicadoresCuit[i];
            }

            var resto = suma % 11;
            var digitoVerificador = 11 - resto;
            if (digitoVerificador == 11)
            {
                digitoVerificador = 0;
            }

            if (digitoVerificador == 10)
            {
                // Caso especial del algoritmo estándar (ver CuitValidator): inválido, se reintenta
                // con la siguiente secuencia.
                continue;
            }

            return cuerpo + digitoVerificador;
        }
    }

    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly WebApplicationFactory<Program> _factory;
    private readonly List<string> _mailsCreados = new();
    private readonly List<Guid> _organizadorIdsCreados = new();

    public BingosControllerTests()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        await using var context = new AppDbContext(options);

        if (_organizadorIdsCreados.Count > 0)
        {
            var bingos = await context.Bingos
                .Where(b => _organizadorIdsCreados.Contains(b.OrganizadorId))
                .ToListAsync();

            // Borrado explícito de Cartones antes que Bingos: aunque el esquema tiene
            // OnDelete(Cascade) (Block 3), este cleanup borra directamente contra el DbContext sin
            // pasar por SQL nativo, así que se respeta el orden de FK explícitamente.
            var bingoIds = bingos.Select(b => b.Id).ToList();
            var cartones = await context.Cartones.Where(c => bingoIds.Contains(c.BingoId)).ToListAsync();
            context.Cartones.RemoveRange(cartones);
            context.Bingos.RemoveRange(bingos);
            await context.SaveChangesAsync();
        }

        if (_mailsCreados.Count > 0)
        {
            var usuarios = await context.Users
                .Where(u => u.Email != null && _mailsCreados.Contains(u.Email))
                .ToListAsync();

            context.Users.RemoveRange(usuarios);
            await context.SaveChangesAsync();
        }

        await _factory.DisposeAsync();
    }

    private string NuevoMail()
    {
        var mail = $"test-bingo-{Guid.NewGuid()}@example.com";
        _mailsCreados.Add(mail);
        return mail;
    }

    private static object CrearBodyRegistro(string mail) => new
    {
        nombreOrganizacion = "Club Social y Deportivo",
        cuit = NuevoCuitValido(),
        mail,
        telefono = TelefonoValido,
        password = PasswordValida,
    };

    private static object CrearBodyBingo(
        string? nombreEvento = "Bingo de prueba",
        DateTime? fechaSorteoUtc = null,
        int cantidadCartones = 10,
        decimal costoPorCarton = 100m)
    {
        return new
        {
            nombreEvento,
            fechaSorteoUtc = (fechaSorteoUtc ?? DateTime.UtcNow.AddDays(5)).ToString("o"),
            cantidadCartones,
            costoPorCarton,
        };
    }

    /// <summary>
    /// Cliente HTTPS dedicado, ya autenticado con un organizador recién registrado y logueado
    /// (cookie <c>bingocart_auth</c>). Registra el organizador en <see cref="_organizadorIdsCreados"/>
    /// para el cleanup de <see cref="DisposeAsync"/>.
    /// </summary>
    private async Task<HttpClient> NuevoClienteAutenticadoAsync()
    {
        var mail = NuevoMail();

        using var clienteAnonimo = _factory.CreateClient();
        var registro = await clienteAnonimo.PostAsJsonAsync("/api/organizadores/registro", CrearBodyRegistro(mail));
        registro.EnsureSuccessStatusCode();

        var registroContent = await registro.Content.ReadFromJsonAsync<RegistrarOrganizadorResponseDto>(DeserializeOptions);
        _organizadorIdsCreados.Add(registroContent!.Id);

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var login = await client.PostAsJsonAsync("/api/organizadores/login", new { mail, password = PasswordValida });
        login.EnsureSuccessStatusCode();

        return client;
    }

    /// <summary>
    /// Siembra un bingo directamente vía <see cref="AppDbContext"/> (mismo mecanismo que
    /// <c>BingoRepositoryTests</c>, spec FEAT-004 Block 1), sin pasar por <c>POST /api/bingos</c>.
    /// Necesario porque FR-06 (mitigación ya cerrada en FEAT-003) impide que un organizador tenga
    /// más de un bingo con <c>FechaSorteoUtc</c> futura a la vez —
    /// <see cref="BingoService.CrearAsync"/> lanza <c>BingoActivoExistenteException</c> en un
    /// segundo <c>POST</c> real para el mismo organizador— así que los tests de este bloque que
    /// necesitan varios bingos del MISMO organizador (orden, paginación) los siembran directo en la
    /// base para ejercitar el LISTADO, no la creación (que ya está cubierta por los tests de
    /// <c>Crear_*</c> de FEAT-003).
    /// </summary>
    private static async Task SeedBingoAsync(
        Guid organizadorId,
        DateTime fechaSorteoUtc,
        DateTime fechaCreacionUtc,
        string nombreEvento = "Bingo de prueba")
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;
        await using var context = new AppDbContext(options);

        var bingo = Bingo.Crear(nombreEvento, fechaSorteoUtc, 10, 100m, organizadorId, fechaCreacionUtc);
        context.Bingos.Add(bingo);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Crear_SinAutenticacion_Devuelve401()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/bingos", CrearBodyBingo());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Crear_ConDatosValidos_Devuelve201YPersiste100CartonesValidosYUnicos()
    {
        using var client = await NuevoClienteAutenticadoAsync();

        var response = await client.PostAsJsonAsync("/api/bingos", CrearBodyBingo(cantidadCartones: 100));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<BingoCreadoResponse>(DeserializeOptions);
        Assert.NotNull(content);
        Assert.NotEqual(Guid.Empty, content!.Id);
        Assert.Equal(100, content.CantidadCartones);

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;
        await using var context = new AppDbContext(options);

        var cartones = await context.Cartones.Where(c => c.BingoId == content.Id).ToListAsync();

        Assert.Equal(100, cartones.Count);
        Assert.Equal(100, cartones.Select(c => c.Id).Distinct().Count());
        Assert.All(cartones, c =>
        {
            Assert.Equal(10, c.Numeros.Count);
            Assert.All(c.Numeros, n => Assert.InRange(n, 1, 90));
        });
    }

    [Fact]
    public async Task Crear_ConCantidadCartonesExcedeLimite_Devuelve400CantidadCartonesExcedeLimite()
    {
        using var client = await NuevoClienteAutenticadoAsync();

        var response = await client.PostAsJsonAsync("/api/bingos", CrearBodyBingo(cantidadCartones: 5001));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponseDto>(DeserializeOptions);
        Assert.NotNull(error);
        Assert.Equal("CantidadCartonesExcedeLimite", error!.Error);
    }

    [Fact]
    public async Task Crear_ConFechaSorteoPasada_Devuelve400FechaSorteoInvalida()
    {
        using var client = await NuevoClienteAutenticadoAsync();

        var response = await client.PostAsJsonAsync(
            "/api/bingos",
            CrearBodyBingo(fechaSorteoUtc: DateTime.UtcNow.AddDays(-1)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponseDto>(DeserializeOptions);
        Assert.NotNull(error);
        Assert.Equal("FechaSorteoInvalida", error!.Error);
    }

    [Fact]
    public async Task Crear_ConBingoVigenteExistente_SegundoIntentoDevuelve409BingoActivoExistente()
    {
        using var client = await NuevoClienteAutenticadoAsync();

        var primero = await client.PostAsJsonAsync("/api/bingos", CrearBodyBingo());
        Assert.Equal(HttpStatusCode.Created, primero.StatusCode);

        var segundo = await client.PostAsJsonAsync("/api/bingos", CrearBodyBingo());

        Assert.Equal(HttpStatusCode.Conflict, segundo.StatusCode);

        var error = await segundo.Content.ReadFromJsonAsync<ErrorResponseDto>(DeserializeOptions);
        Assert.NotNull(error);
        Assert.Equal("BingoActivoExistente", error!.Error);
    }

    [Fact]
    public async Task Crear_Con5000Cartones_CompletaEnMenosDe10SegundosSinDosCartonesConLosMismosNumeros()
    {
        using var client = await NuevoClienteAutenticadoAsync();

        var stopwatch = Stopwatch.StartNew();
        var response = await client.PostAsJsonAsync("/api/bingos", CrearBodyBingo(cantidadCartones: 5000));
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"La creación tardó {stopwatch.Elapsed}.");

        var content = await response.Content.ReadFromJsonAsync<BingoCreadoResponse>(DeserializeOptions);
        Assert.NotNull(content);
        Assert.Equal(5000, content!.CantidadCartones);

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;
        await using var context = new AppDbContext(options);

        var cartones = await context.Cartones.Where(c => c.BingoId == content.Id).ToListAsync();

        Assert.Equal(5000, cartones.Count);

        var conjuntosCanonicos = cartones.Select(c => string.Join(",", c.Numeros)).ToList();
        Assert.Equal(5000, conjuntosCanonicos.Distinct().Count());
    }

    [Fact]
    public async Task Crear_Con3IntentosPreviosEnLaVentana_CuartoIntentoDevuelve429()
    {
        using var client = await NuevoClienteAutenticadoAsync();

        // Datos inválidos (cantidad excedida) a propósito, para no chocar con la regla de bingo
        // activo (FR-06) y ejercitar puramente el rate limiter (mitigación TM-01).
        for (var intento = 1; intento <= 3; intento++)
        {
            var respuesta = await client.PostAsJsonAsync("/api/bingos", CrearBodyBingo(cantidadCartones: 5001));
            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        }

        var cuartoIntento = await client.PostAsJsonAsync("/api/bingos", CrearBodyBingo(cantidadCartones: 5001));

        Assert.Equal(HttpStatusCode.TooManyRequests, cuartoIntento.StatusCode);
    }

    [Fact]
    public async Task Listar_SinAutenticacion_Devuelve401()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/bingos");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Listar_ConDosBingosDelOrganizador_Devuelve200ConAmbosOrdenadosPorFechaCreacionDescendente()
    {
        using var client = await NuevoClienteAutenticadoAsync();
        var organizadorId = _organizadorIdsCreados[^1];
        var ahoraUtc = DateTime.UtcNow;

        await SeedBingoAsync(organizadorId, ahoraUtc.AddDays(5), ahoraUtc, nombreEvento: "Bingo mas antiguo");
        await SeedBingoAsync(
            organizadorId, ahoraUtc.AddDays(6), ahoraUtc.AddMinutes(1), nombreEvento: "Bingo mas reciente");

        var response = await client.GetAsync("/api/bingos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<BingoListadoResponse>(DeserializeOptions);
        Assert.NotNull(content);
        Assert.Equal(2, content!.Items.Count);
        Assert.Equal("Bingo mas reciente", content.Items[0].NombreEvento);
        Assert.Equal("Bingo mas antiguo", content.Items[1].NombreEvento);
        Assert.Equal(2, content.Total);
    }

    [Fact]
    public async Task Listar_ConSieteBingosYPage2PageSize5_Devuelve200ConLosDosRestantesYTotalPaginasDos()
    {
        using var client = await NuevoClienteAutenticadoAsync();
        var organizadorId = _organizadorIdsCreados[^1];
        var ahoraUtc = DateTime.UtcNow;

        for (var i = 0; i < 7; i++)
        {
            await SeedBingoAsync(
                organizadorId, ahoraUtc.AddDays(5 + i), ahoraUtc.AddMinutes(i), nombreEvento: $"Bingo {i}");
        }

        var response = await client.GetAsync("/api/bingos?page=2&pageSize=5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<BingoListadoResponse>(DeserializeOptions);
        Assert.NotNull(content);
        Assert.Equal(2, content!.Items.Count);
        Assert.Equal(7, content.Total);
        Assert.Equal(2, content.TotalPaginas);
        Assert.Equal(2, content.Page);
        Assert.Equal(5, content.PageSize);
        // Orden descendente por FechaCreacionUtc: i=6..0. Page 1 (pageSize 5) = i=6,5,4,3,2. Page 2 =
        // las posiciones 6 y 7 restantes = i=1, i=0.
        Assert.Equal("Bingo 1", content.Items[0].NombreEvento);
        Assert.Equal("Bingo 0", content.Items[1].NombreEvento);
    }

    [Fact]
    public async Task Listar_ConPageCero_Devuelve400DatosInvalidos()
    {
        using var client = await NuevoClienteAutenticadoAsync();

        var response = await client.GetAsync("/api/bingos?page=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponseDto>(DeserializeOptions);
        Assert.NotNull(error);
        Assert.Equal("DatosInvalidos", error!.Error);
    }

    [Fact]
    public async Task Listar_ConPageSizeNoNumerico_Devuelve400DatosInvalidosYConfirmaBindingPorConstructorDelRecord()
    {
        using var client = await NuevoClienteAutenticadoAsync();

        var response = await client.GetAsync("/api/bingos?pageSize=abc");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponseDto>(DeserializeOptions);
        Assert.NotNull(error);
        Assert.Equal("DatosInvalidos", error!.Error);
    }

    [Fact]
    public async Task Listar_ConBingoDeOtroOrganizador_Devuelve200ConItemsVacioYTotalCero()
    {
        using var clienteA = await NuevoClienteAutenticadoAsync();
        var creacionA = await clienteA.PostAsJsonAsync("/api/bingos", CrearBodyBingo());
        Assert.Equal(HttpStatusCode.Created, creacionA.StatusCode);

        using var clienteB = await NuevoClienteAutenticadoAsync();
        var response = await clienteB.GetAsync("/api/bingos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<BingoListadoResponse>(DeserializeOptions);
        Assert.NotNull(content);
        Assert.Empty(content!.Items);
        Assert.Equal(0, content.Total);
    }

    [Fact]
    public async Task Listar_SinBingosCreados_Devuelve200ConItemsVacioYTotalCero()
    {
        using var client = await NuevoClienteAutenticadoAsync();

        var response = await client.GetAsync("/api/bingos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<BingoListadoResponse>(DeserializeOptions);
        Assert.NotNull(content);
        Assert.Empty(content!.Items);
        Assert.Equal(0, content.Total);
    }

    private sealed record RegistrarOrganizadorResponseDto(Guid Id, string NombreOrganizacion, string Mail);

    private sealed record ErrorResponseDto(string Error, string Message);
}
