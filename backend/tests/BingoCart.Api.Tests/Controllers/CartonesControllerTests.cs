using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using BingoCart.Domain.Bingos;
using BingoCart.Infrastructure.Data;
using BingoCart.Infrastructure.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace BingoCart.Api.Tests.Controllers;

/// <summary>
/// Tests de integración de <c>GET /api/cartones/descubrimiento</c> y
/// <c>GET /api/cartones/organizador/{organizadorId}</c> (spec FEAT-008a, Block 2) contra el stack
/// completo levantado en memoria (<see cref="WebApplicationFactory{T}"/>) y el SQL Server real
/// dockerizado (puerto 14330) — mismo patrón que <c>BingosControllerTests</c>/
/// <c>OrganizadoresControllerTests</c>: organizador+bingo+cartones sembrados directo contra
/// <see cref="AppDbContext"/> (mismo mecanismo que
/// <c>OrganizadoresControllerTests.SembrarOrganizadorConBingoActivoAsync</c>), sin pasar por
/// registro/login — ambos endpoints son <c>[AllowAnonymous]</c>, no hace falta un organizador
/// autenticado para ejercitarlos.
/// </summary>
public sealed class CartonesControllerTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Server=localhost,14330;Database=BingoCart;User Id=sa;" +
        "Password=BingoCart_Dev2026!;TrustServerCertificate=True;Encrypt=True;";

    private const string TelefonoValido = "+54 11 4444-5555";

    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly List<string> _mailsCreados = new();
    private readonly List<Guid> _organizadorIdsCreados = new();

    public CartonesControllerTests()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        _client = _factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

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

        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // Genera `cantidad` cartones únicos y válidos para `bingoId` (mismo mecanismo que
    // DescubrimientoRepositoryTests.NuevosCartones): ventana deslizante de 10 números en 1-90, sin
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
    /// Siembra un organizador + un bingo (activo o no, según <paramref name="fechaSorteoUtc"/>) +
    /// sus cartones directo contra <see cref="AppDbContext"/> — mismo mecanismo que
    /// <c>OrganizadoresControllerTests.SembrarOrganizadorConBingoActivoAsync</c>, extendido con la
    /// creación de cartones reales (necesarios para ejercitar los endpoints de descubrimiento).
    /// </summary>
    private async Task<(Guid OrganizadorId, Guid BingoId, string NombreEvento)> SembrarOrganizadorConBingoYCartonesAsync(
        string nombreOrganizacion,
        DateTime fechaSorteoUtc,
        DateTime ahoraUtc,
        int cantidadCartones,
        string? cuit = null,
        string? mail = null,
        string? telefono = null,
        string nombreEvento = "Bingo de prueba")
    {
        var organizadorId = Guid.NewGuid();
        var mailReal = mail ?? $"test-cartones-{organizadorId}@example.com";

        var organizador = new ApplicationUser
        {
            Id = organizadorId,
            UserName = mailReal,
            Email = mailReal,
            NombreOrganizacion = nombreOrganizacion,
            Cuit = cuit ?? organizadorId.ToString("N")[..11],
            Telefono = telefono ?? TelefonoValido,
        };
        var bingo = Bingo.Crear(nombreEvento, fechaSorteoUtc, cantidadCartones, 100m, organizadorId, ahoraUtc);

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;
        await using var context = new AppDbContext(options);
        context.Users.Add(organizador);
        context.Bingos.Add(bingo);
        context.Cartones.AddRange(NuevosCartones(bingo.Id, cantidadCartones));
        await context.SaveChangesAsync();

        _organizadorIdsCreados.Add(organizadorId);
        _mailsCreados.Add(mailReal);

        return (organizadorId, bingo.Id, nombreEvento);
    }

    // Mueve la FechaSorteoUtc de un bingo ya persistido al pasado (mismo mecanismo que
    // DescubrimientoRepositoryTests) — Bingo.Crear no permite fechas pasadas, así que un bingo
    // "vencido" se construye válido y se vence después, directo contra la fila.
    private static async Task VencerBingoAsync(Guid bingoId, DateTime fechaPasadaUtc)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;
        await using var context = new AppDbContext(options);
        var bingo = await context.Bingos.SingleAsync(b => b.Id == bingoId);
        context.Entry(bingo).Property(nameof(Bingo.FechaSorteoUtc)).CurrentValue = fechaPasadaUtc;
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Descubrimiento_ConDosOrganizadoresConBingoActivoYCartonesReales_Devuelve200ConHastaCincoCartonesNoMezclados()
    {
        var ahoraUtc = DateTime.UtcNow;
        var (_, _, nombreEventoUno) = await SembrarOrganizadorConBingoYCartonesAsync(
            "Club Descubrimiento Uno", ahoraUtc.AddDays(10), ahoraUtc, cantidadCartones: 5,
            nombreEvento: "Bingo Descubrimiento Uno");
        var (_, _, nombreEventoDos) = await SembrarOrganizadorConBingoYCartonesAsync(
            "Club Descubrimiento Dos", ahoraUtc.AddDays(11), ahoraUtc, cantidadCartones: 5,
            nombreEvento: "Bingo Descubrimiento Dos");

        var response = await _client.GetAsync("/api/cartones/descubrimiento");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<List<CartonDescubiertoResponseDto>>(DeserializeOptions);
        Assert.NotNull(content);
        Assert.InRange(content!.Count, 0, 5);

        Assert.All(content, c =>
        {
            var esOrganizadorUno = c.NombreOrganizacion == "Club Descubrimiento Uno" && c.NombreEvento == nombreEventoUno;
            var esOrganizadorDos = c.NombreOrganizacion == "Club Descubrimiento Dos" && c.NombreEvento == nombreEventoDos;

            // Nunca mezclado: el nombre de organización y el nombre de evento tienen que
            // corresponder al MISMO organizador sembrado, no a una combinación cruzada.
            Assert.True(
                esOrganizadorUno || esOrganizadorDos,
                $"Combinación inesperada: {c.NombreOrganizacion} / {c.NombreEvento}");
        });
    }

    [Fact]
    public async Task Descubrimiento_SinNingunBingoActivo_Devuelve200ConListaVacia()
    {
        var response = await _client.GetAsync("/api/cartones/descubrimiento");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<List<CartonDescubiertoResponseDto>>(DeserializeOptions);
        Assert.NotNull(content);
        Assert.Empty(content!);
    }

    [Fact]
    public async Task PorOrganizador_ConOrganizadorConBingoActivoYCartonesReales_Devuelve200ConHastaCincoCartonesTodosDelBingo()
    {
        var ahoraUtc = DateTime.UtcNow;
        var (organizadorId, _, nombreEvento) = await SembrarOrganizadorConBingoYCartonesAsync(
            "Club Por Organizador", ahoraUtc.AddDays(5), ahoraUtc, cantidadCartones: 8,
            nombreEvento: "Bingo Por Organizador");

        var response = await _client.GetAsync($"/api/cartones/organizador/{organizadorId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<List<CartonDescubiertoResponseDto>>(DeserializeOptions);
        Assert.NotNull(content);
        Assert.InRange(content!.Count, 1, 5);
        Assert.All(content, c =>
        {
            Assert.Equal("Club Por Organizador", c.NombreOrganizacion);
            Assert.Equal(nombreEvento, c.NombreEvento);
        });
    }

    [Fact]
    public async Task PorOrganizador_ConGuidInexistente_Devuelve404OrganizadorNoEncontrado()
    {
        var response = await _client.GetAsync($"/api/cartones/organizador/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponseDto>(DeserializeOptions);
        Assert.NotNull(error);
        Assert.Equal("OrganizadorNoEncontrado", error!.Error);
    }

    [Fact]
    public async Task PorOrganizador_ConOrganizadorConBingoDeSorteoPasado_Devuelve200ConListaVacia()
    {
        var ahoraUtc = DateTime.UtcNow;
        var (organizadorId, bingoId, _) = await SembrarOrganizadorConBingoYCartonesAsync(
            "Club Bingo Vencido", ahoraUtc.AddDays(5), ahoraUtc, cantidadCartones: 3);
        await VencerBingoAsync(bingoId, ahoraUtc.AddDays(-1));

        var response = await _client.GetAsync($"/api/cartones/organizador/{organizadorId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<List<CartonDescubiertoResponseDto>>(DeserializeOptions);
        Assert.NotNull(content);
        Assert.Empty(content!);
    }

    [Fact]
    public async Task PorOrganizador_ConOrganizadorConCuitMailTelefono_LaRespuestaCrudaNoContieneEsosDatos()
    {
        var ahoraUtc = DateTime.UtcNow;
        var idAuxiliar = Guid.NewGuid();
        var mailSensible = $"sensible-cartones-{idAuxiliar}@example.com";
        var cuitSensible = idAuxiliar.ToString("N")[..11];
        var telefonoSensible = "+54 11 9999-7777";

        var (organizadorId, _, _) = await SembrarOrganizadorConBingoYCartonesAsync(
            "Club Con Datos Sensibles Cartones",
            ahoraUtc.AddDays(4),
            ahoraUtc,
            cantidadCartones: 3,
            cuit: cuitSensible,
            mail: mailSensible,
            telefono: telefonoSensible);

        var response = await _client.GetAsync($"/api/cartones/organizador/{organizadorId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rawBody = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(cuitSensible, rawBody);
        Assert.DoesNotContain(mailSensible, rawBody);
        Assert.DoesNotContain(telefonoSensible, rawBody);
    }

    [Fact]
    public async Task Descubrimiento_Con60RequestsPreviasEnLaVentana_Request61Devuelve429()
    {
        for (var intento = 1; intento <= 60; intento++)
        {
            var respuesta = await _client.GetAsync("/api/cartones/descubrimiento");
            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        }

        var request61 = await _client.GetAsync("/api/cartones/descubrimiento");

        Assert.Equal(HttpStatusCode.TooManyRequests, request61.StatusCode);
    }

    private sealed record CartonDescubiertoResponseDto(
        Guid Id,
        string NombreOrganizacion,
        string NombreEvento,
        DateTime FechaSorteoUtc,
        decimal CostoPorCarton,
        IReadOnlyList<int> Numeros);

    private sealed record ErrorResponseDto(string Error, string Message);
}
