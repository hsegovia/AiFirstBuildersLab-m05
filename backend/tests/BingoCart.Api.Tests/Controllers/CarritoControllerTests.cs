using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BingoCart.Domain.Bingos;
using BingoCart.Infrastructure.Data;
using BingoCart.Infrastructure.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace BingoCart.Api.Tests.Controllers;

/// <summary>
/// Tests de integración de <c>CarritoController</c> (spec FEAT-008b, Block 3) contra el stack
/// completo levantado en memoria (<see cref="WebApplicationFactory{T}"/>), el SQL Server real
/// dockerizado (puerto 14330) y el Redis real dockerizado (puerto 16379) — mismo patrón que
/// <c>CartonesControllerTests</c> (FEAT-008a): organizador+bingo+cartones sembrados directo contra
/// <see cref="AppDbContext"/>, sin pasar por registro/login (los 4 endpoints son
/// <c>[AllowAnonymous]</c>). La cookie <c>bingocart_carrito</c> es <c>Secure</c> (mismo criterio ya
/// documentado en <c>BingosControllerTests</c> para <c>bingocart_auth</c>), así que todo cliente que
/// necesite conservar su sesión entre requests usa un <see cref="WebApplicationFactoryClientOptions"/>
/// con <c>BaseAddress = https://localhost</c> — sin esto, <see cref="CookieContainer"/> guarda la
/// cookie pero nunca la reenvía sobre una conexión http. Dos "sesiones distintas" se simulan con dos
/// instancias separadas de <see cref="HttpClient"/> (cada una con su propio
/// <see cref="System.Net.CookieContainer"/> interno, nunca compartido entre sí) — nunca reutilizando
/// el mismo cliente para representar a dos participantes.
///
/// Limpieza de Redis (corrección puntual, revisión de Block 3, Hallazgo 1): cada test que agrega
/// cartones o pide una nueva tanda hace que el servidor escriba en Redis (<c>carrito:{sesionId}</c>,
/// <c>reservado:carton:{cartonId}</c>, <c>descartados:{sesionId}</c>) — mismo patrón de "cada test
/// borra ÚNICAMENTE lo que él mismo creó" que <c>CarritoRepositoryTests</c> (Block 1, citado ahí
/// como "Rule #0" de testing.instructions.md), pero acá el <c>sesionId</c> no lo elige el test: lo
/// genera el servidor (token CSPRNG) y viaja en el header <c>Set-Cookie</c> de la primera respuesta
/// exitosa de cada sesión. <see cref="RegistrarLimpiezaRedis"/> lee ese header y registra las claves
/// a borrar en <see cref="DisposeAsync"/>, nunca dependiendo de que el TTL las libere solo.
/// </summary>
public sealed class CarritoControllerTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Server=localhost,14330;Database=BingoCart;User Id=sa;" +
        "Password=BingoCart_Dev2026!;TrustServerCertificate=True;Encrypt=True;";

    // Mismo puerto remapeado que docker-compose.yml y mismo connection string que
    // CarritoRepositoryTests (Block 1) — Redis real de desarrollo local, nunca un mock.
    private const string RedisConnectionString = "localhost:16379";

    private const string CookieCarritoName = "bingocart_carrito";

    private const string TelefonoValido = "+54 11 4444-5555";

    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly WebApplicationFactory<Program> _factory;
    private readonly List<Guid> _organizadorIdsCreados = new();
    private readonly List<string> _mailsCreados = new();
    private readonly List<string> _clavesRedisABorrar = new();
    private ConnectionMultiplexer _redisConnectionMultiplexer = null!;

    public CarritoControllerTests()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    public async Task InitializeAsync()
    {
        _redisConnectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(RedisConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (_organizadorIdsCreados.Count > 0 || _mailsCreados.Count > 0)
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
        }

        var db = _redisConnectionMultiplexer.GetDatabase();
        foreach (var clave in _clavesRedisABorrar)
        {
            await db.KeyDeleteAsync(clave);
        }

        await _redisConnectionMultiplexer.DisposeAsync();
        await _factory.DisposeAsync();
    }

    // Lee el sesionId (token CSPRNG opaco) que el servidor generó y devolvió en el header
    // Set-Cookie de `response` — el test nunca lo elige, así que no hay otra forma de saber qué
    // claves de Redis escribió ese request. Registra `carrito:{sesionId}` y `descartados:{sesionId}`
    // siempre (borrarlas si no existen es un no-op) y `reservado:carton:{cartonId}` por cada cartón
    // que el test haya agregado en esa sesión.
    private void RegistrarLimpiezaRedis(HttpResponseMessage response, params Guid[] cartonIdsAgregados)
    {
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies));
        var cookie = cookies!.Single(c => c.StartsWith($"{CookieCarritoName}=", StringComparison.Ordinal));
        var valorConAtributos = cookie[(CookieCarritoName.Length + 1)..];
        var finDelValor = valorConAtributos.IndexOf(';');
        var valorCrudo = finDelValor >= 0 ? valorConAtributos[..finDelValor] : valorConAtributos;
        // Response.Cookies.Append escribe el valor URL-encoded en el header (el token CSPRNG en
        // base64 tiene '+'/'/'/'=' que ASP.NET Core percent-encodea al fijar la cookie: p.ej. '/' →
        // '%2F') — hay que decodificarlo para reconstruir el sesionId EXACTO que el servidor usó al
        // armar las claves de Redis, o `KeyDeleteAsync` apunta a una clave que nunca existió y no
        // borra nada.
        var sesionId = Uri.UnescapeDataString(valorCrudo);

        _clavesRedisABorrar.Add($"carrito:{sesionId}");
        _clavesRedisABorrar.Add($"descartados:{sesionId}");
        foreach (var cartonId in cartonIdsAgregados)
        {
            _clavesRedisABorrar.Add($"reservado:carton:{cartonId}");
        }
    }

    // Cliente HTTPS dedicado (BaseAddress https://localhost): la cookie bingocart_carrito es
    // Secure, así que un cliente http normal la recibiría pero nunca la reenviaría en el siguiente
    // request — mismo mecanismo ya documentado en BingosControllerTests para bingocart_auth.
    private HttpClient NuevoClienteConCookieHttps() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

    // Genera `cantidad` cartones únicos y válidos para `bingoId` (mismo mecanismo que
    // CartonesControllerTests.NuevosCartones): ventana deslizante de 10 números en 1-90, sin
    // colisionar con el índice único (BingoId, Numeros).
    private static List<Carton> NuevosCartones(Guid bingoId, int cantidad)
    {
        var cartones = new List<Carton>();
        for (var i = 0; i < cantidad; i++)
        {
            var numeros = Enumerable.Range(1 + i, 10).ToArray();
            cartones.Add(Carton.Crear(bingoId, numeros));
        }

        return cartones;
    }

    /// <summary>
    /// Siembra un organizador + un bingo activo + sus cartones directo contra
    /// <see cref="AppDbContext"/> (mismo mecanismo que
    /// <c>CartonesControllerTests.SembrarOrganizadorConBingoYCartonesAsync</c>), devolviendo también
    /// los <c>Guid</c> de cada cartón sembrado para que los tests puedan elegir cuáles agregar/
    /// descartar.
    /// </summary>
    private async Task<(Guid OrganizadorId, Guid BingoId, string NombreEvento, string NombreOrganizacion, decimal CostoPorCarton, List<Guid> CartonIds)>
        SembrarOrganizadorConBingoYCartonesAsync(
            string nombreOrganizacion,
            int cantidadCartones,
            decimal costoPorCarton = 100m,
            string nombreEvento = "Bingo de prueba")
    {
        var ahoraUtc = DateTime.UtcNow;
        var organizadorId = Guid.NewGuid();
        var mail = $"test-carrito-{organizadorId}@example.com";

        var organizador = new ApplicationUser
        {
            Id = organizadorId,
            UserName = mail,
            Email = mail,
            NombreOrganizacion = nombreOrganizacion,
            Cuit = organizadorId.ToString("N")[..11],
            Telefono = TelefonoValido,
        };
        var bingo = Bingo.Crear(nombreEvento, ahoraUtc.AddDays(10), cantidadCartones, costoPorCarton, organizadorId, ahoraUtc);
        var cartones = NuevosCartones(bingo.Id, cantidadCartones);

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;
        await using var context = new AppDbContext(options);
        context.Users.Add(organizador);
        context.Bingos.Add(bingo);
        context.Cartones.AddRange(cartones);
        await context.SaveChangesAsync();

        _organizadorIdsCreados.Add(organizadorId);
        _mailsCreados.Add(mail);

        return (organizadorId, bingo.Id, nombreEvento, nombreOrganizacion, costoPorCarton, cartones.Select(c => c.Id).ToList());
    }

    [Fact]
    public async Task Agregar_ConCartonRealSinCookiePrevia_Devuelve204YFijaLaCookieDeSesion()
    {
        var (_, _, _, _, _, cartonIds) = await SembrarOrganizadorConBingoYCartonesAsync("Club Agregar Uno", 3);
        using var client = NuevoClienteConCookieHttps();

        var response = await client.PostAsync($"/api/carrito/cartones/{cartonIds[0]}", content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies));
        Assert.Contains(cookies!, c => c.StartsWith("bingocart_carrito=", StringComparison.Ordinal));
        RegistrarLimpiezaRedis(response, cartonIds[0]);
    }

    [Fact]
    public async Task Agregar_ConCartonInexistente_Devuelve404CartonInexistente()
    {
        using var client = NuevoClienteConCookieHttps();

        var response = await client.PostAsync($"/api/carrito/cartones/{Guid.NewGuid()}", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponseDto>(DeserializeOptions);
        Assert.NotNull(error);
        Assert.Equal("CartonInexistente", error!.Error);
    }

    [Fact]
    public async Task Agregar_ConDosSesionesDistintasSobreElMismoCarton_LaSegundaDevuelve409CartonYaReservado()
    {
        var (_, _, _, _, _, cartonIds) = await SembrarOrganizadorConBingoYCartonesAsync("Club Agregar Dos", 1);
        using var clienteUno = NuevoClienteConCookieHttps();
        using var clienteDos = NuevoClienteConCookieHttps();

        var primero = await clienteUno.PostAsync($"/api/carrito/cartones/{cartonIds[0]}", content: null);
        var segundo = await clienteDos.PostAsync($"/api/carrito/cartones/{cartonIds[0]}", content: null);

        Assert.Equal(HttpStatusCode.NoContent, primero.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, segundo.StatusCode);
        var error = await segundo.Content.ReadFromJsonAsync<ErrorResponseDto>(DeserializeOptions);
        Assert.NotNull(error);
        Assert.Equal("CartonYaReservado", error!.Error);
        // La sesión perdedora (segundo) nunca escribe en Redis (IntentarAgregarAsync devuelve false
        // antes de tocar el carrito), así que solo hay que limpiar la sesión ganadora.
        RegistrarLimpiezaRedis(primero, cartonIds[0]);
    }

    [Fact]
    public async Task Ver_ConDosCartonesAgregadosEnLaMismaSesion_Devuelve200ConDosItemsYElMontoTotalCorrecto()
    {
        var (_, _, _, _, costoPorCarton, cartonIds) = await SembrarOrganizadorConBingoYCartonesAsync("Club Ver Dos", 2, costoPorCarton: 150m);
        using var client = NuevoClienteConCookieHttps();

        var primerAgregado = await client.PostAsync($"/api/carrito/cartones/{cartonIds[0]}", content: null);
        primerAgregado.EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/carrito/cartones/{cartonIds[1]}", content: null)).EnsureSuccessStatusCode();

        var response = await client.GetAsync("/api/carrito");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var carrito = await response.Content.ReadFromJsonAsync<CarritoResponseDto>(DeserializeOptions);
        Assert.NotNull(carrito);
        Assert.Equal(2, carrito!.Items.Count);
        Assert.Equal(2, carrito.CantidadTotal);
        Assert.Equal(costoPorCarton * 2, carrito.MontoTotal);
        // La cookie de sesión se fija en la PRIMERA respuesta exitosa (el segundo POST y el GET la
        // reutilizan sin volver a fijarla).
        RegistrarLimpiezaRedis(primerAgregado, cartonIds[0], cartonIds[1]);
    }

    [Fact]
    public async Task Ver_SinCookiePrevia_Devuelve200ConCarritoVacio()
    {
        using var client = NuevoClienteConCookieHttps();

        var response = await client.GetAsync("/api/carrito");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var carrito = await response.Content.ReadFromJsonAsync<CarritoResponseDto>(DeserializeOptions);
        Assert.NotNull(carrito);
        Assert.Empty(carrito!.Items);
        Assert.Equal(0, carrito.CantidadTotal);
        RegistrarLimpiezaRedis(response);
    }

    [Fact]
    public async Task Quitar_ConUnCartonYaAgregado_Devuelve204YLuegoElCarritoQuedaVacio()
    {
        var (_, _, _, _, _, cartonIds) = await SembrarOrganizadorConBingoYCartonesAsync("Club Quitar Uno", 1);
        using var client = NuevoClienteConCookieHttps();
        var agregado = await client.PostAsync($"/api/carrito/cartones/{cartonIds[0]}", content: null);
        agregado.EnsureSuccessStatusCode();

        var quitar = await client.DeleteAsync($"/api/carrito/cartones/{cartonIds[0]}");
        var ver = await client.GetAsync("/api/carrito");

        Assert.Equal(HttpStatusCode.NoContent, quitar.StatusCode);
        var carrito = await ver.Content.ReadFromJsonAsync<CarritoResponseDto>(DeserializeOptions);
        Assert.NotNull(carrito);
        Assert.Empty(carrito!.Items);
        RegistrarLimpiezaRedis(agregado, cartonIds[0]);
    }

    [Fact]
    public async Task Quitar_ConUnCartonQueNuncaEstuvoEnElCarrito_Devuelve204Igualmente()
    {
        using var client = NuevoClienteConCookieHttps();

        var response = await client.DeleteAsync($"/api/carrito/cartones/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        RegistrarLimpiezaRedis(response);
    }

    [Fact]
    public async Task NuevaTanda_ConOrganizadorIdNuloYCincoDescartados_Devuelve200SinNingunoDeLosDescartados()
    {
        var (_, _, _, _, _, cartonIds) = await SembrarOrganizadorConBingoYCartonesAsync("Club Tanda Global", 10);
        var descartados = cartonIds.Take(5).ToList();
        using var client = NuevoClienteConCookieHttps();

        var response = await client.PostAsJsonAsync(
            "/api/carrito/tandas/nueva",
            new { organizadorId = (Guid?)null, cartonIdsDescartados = descartados });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var resultado = await response.Content.ReadFromJsonAsync<List<CartonDescubiertoResponseDto>>(DeserializeOptions);
        Assert.NotNull(resultado);
        Assert.InRange(resultado!.Count, 0, 5);
        Assert.DoesNotContain(resultado, c => descartados.Contains(c.Id));
        RegistrarLimpiezaRedis(response);
    }

    [Fact]
    public async Task NuevaTanda_SinCartonIdsDescartadosEnElBody_Devuelve200SinFiltroDeExclusion()
    {
        using var client = NuevoClienteConCookieHttps();

        // Body con `cartonIdsDescartados` omitido por completo (no `[]`, directamente ausente) —
        // System.Text.Json deja la propiedad en null pese a que el record la declara
        // IReadOnlyList<Guid> no-nullable: el chequeo de nullable reference types es solo de
        // compilación, no lo aplica el deserializador (Hallazgo 2, revisión de Block 3).
        var response = await client.PostAsJsonAsync(
            "/api/carrito/tandas/nueva",
            new { organizadorId = (Guid?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var resultado = await response.Content.ReadFromJsonAsync<List<CartonDescubiertoResponseDto>>(DeserializeOptions);
        Assert.NotNull(resultado);
        Assert.InRange(resultado!.Count, 0, 5);
        RegistrarLimpiezaRedis(response);
    }

    [Fact]
    public async Task NuevaTanda_ConOrganizadorIdRealYTresDescartados_Devuelve200SoloDeEseBingoSinLosDescartados()
    {
        var (organizadorId, _, nombreEvento, nombreOrganizacion, _, cartonIds) =
            await SembrarOrganizadorConBingoYCartonesAsync("Club Tanda Por Organizador", 8);
        var descartados = cartonIds.Take(3).ToList();
        using var client = NuevoClienteConCookieHttps();

        var response = await client.PostAsJsonAsync(
            "/api/carrito/tandas/nueva",
            new { organizadorId = (Guid?)organizadorId, cartonIdsDescartados = descartados });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var resultado = await response.Content.ReadFromJsonAsync<List<CartonDescubiertoResponseDto>>(DeserializeOptions);
        Assert.NotNull(resultado);
        Assert.InRange(resultado!.Count, 1, 5);
        Assert.DoesNotContain(resultado, c => descartados.Contains(c.Id));
        Assert.All(resultado, c =>
        {
            Assert.Equal(nombreOrganizacion, c.NombreOrganizacion);
            Assert.Equal(nombreEvento, c.NombreEvento);
        });
        RegistrarLimpiezaRedis(response);
    }

    [Fact]
    public async Task NuevaTanda_ConOrganizadorIdInexistente_Devuelve404OrganizadorNoEncontrado()
    {
        // F-VER-04: `organizadorId` sin organizador registrado — sad-path del Método 2 sin
        // ningún test hasta ahora, ni unitario ni de integración.
        using var client = NuevoClienteConCookieHttps();

        var response = await client.PostAsJsonAsync(
            "/api/carrito/tandas/nueva",
            new { organizadorId = (Guid?)Guid.NewGuid(), cartonIdsDescartados = Array.Empty<Guid>() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponseDto>(DeserializeOptions);
        Assert.NotNull(error);
        Assert.Equal("OrganizadorNoEncontrado", error!.Error);
        // Sin RegistrarLimpiezaRedis: CarritoService.PedirNuevaTandaPorOrganizadorAsync lanza
        // OrganizadorNoEncontradoException ANTES de invocar PrepararExclusionAsync (el único punto
        // de este método que escribe en Redis, vía AgregarDescartadosAsync) — la cookie de sesión sí
        // se fija (ObtenerOCrearSesionId corre al inicio de NuevaTanda, antes de llamar al servicio),
        // pero no llega a crearse ninguna clave "carrito:{sesionId}" ni "descartados:{sesionId}" que
        // limpiar. Confirmado corriendo este test con `redis-cli DBSIZE` antes/después: sin cambios.
    }

    [Fact]
    public async Task NuevaTanda_Con51CartonIdsDescartados_Devuelve400CantidadDescartadosExcedeLimite()
    {
        // Corrección post-SAST (F-SAST-14): 51 elementos supera el límite (50) — la validación corta
        // antes de tocar Redis/SQL, así que no hace falta sembrar ningún bingo real.
        var descartados = Enumerable.Range(0, 51).Select(_ => Guid.NewGuid()).ToList();
        using var client = NuevoClienteConCookieHttps();

        var response = await client.PostAsJsonAsync(
            "/api/carrito/tandas/nueva",
            new { organizadorId = (Guid?)null, cartonIdsDescartados = descartados });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponseDto>(DeserializeOptions);
        Assert.NotNull(error);
        Assert.Equal("CantidadDescartadosExcedeLimite", error!.Error);
    }

    [Fact]
    public async Task NuevaTanda_ConExactamente50CartonIdsDescartados_Devuelve200NoRechazado()
    {
        // Límite exacto (50, no "muchos"): el request debe pasar de largo, no ser rechazado.
        var descartados = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToList();
        using var client = NuevoClienteConCookieHttps();

        var response = await client.PostAsJsonAsync(
            "/api/carrito/tandas/nueva",
            new { organizadorId = (Guid?)null, cartonIdsDescartados = descartados });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        RegistrarLimpiezaRedis(response);
    }

    [Fact]
    public async Task Ver_Con60RequestsPreviasEnLaVentana_Request61Devuelve429()
    {
        using var client = NuevoClienteConCookieHttps();
        HttpResponseMessage? primeraRespuesta = null;

        for (var intento = 1; intento <= 60; intento++)
        {
            var respuesta = await client.GetAsync("/api/carrito");
            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
            primeraRespuesta ??= respuesta;
        }

        var request61 = await client.GetAsync("/api/carrito");

        Assert.Equal(HttpStatusCode.TooManyRequests, request61.StatusCode);
        // La cookie de sesión se fija en el primer GET del loop (index 1); el resto la reutiliza.
        RegistrarLimpiezaRedis(primeraRespuesta!);
    }

    private sealed record ErrorResponseDto(string Error, string Message);

    private sealed record ItemCarritoResponseDto(Guid CartonId, string NombreOrganizacion, string NombreEvento, decimal PrecioUnitario);

    private sealed record CarritoResponseDto(List<ItemCarritoResponseDto> Items, int CantidadTotal, decimal MontoTotal);

    private sealed record CartonDescubiertoResponseDto(
        Guid Id,
        string NombreOrganizacion,
        string NombreEvento,
        DateTime FechaSorteoUtc,
        decimal CostoPorCarton,
        IReadOnlyList<int> Numeros);
}
