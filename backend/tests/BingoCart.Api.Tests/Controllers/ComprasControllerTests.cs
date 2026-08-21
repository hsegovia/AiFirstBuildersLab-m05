using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BingoCart.Domain.Bingos;
using BingoCart.Infrastructure.Auth;
using BingoCart.Infrastructure.Data;
using BingoCart.Infrastructure.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace BingoCart.Api.Tests.Controllers;

/// <summary>
/// Tests de integración de <c>ComprasController</c> (spec FEAT-009a, Block 3) contra el stack
/// completo levantado en memoria (<see cref="WebApplicationFactory{T}"/>), el SQL Server real
/// dockerizado (puerto 14330) y el Redis real dockerizado (puerto 16379) — mismo patrón que
/// <c>CarritoControllerTests</c> (FEAT-008b): organizador+bingo+cartones sembrados directo contra
/// <see cref="AppDbContext"/>, comprador registrado/logueado vía los endpoints HTTP reales de
/// <c>CompradoresController</c>. Ambas cookies (<c>bingocart_auth</c>/<c>bingocart_carrito</c>) son
/// <c>Secure</c>, así que todo cliente usa <c>BaseAddress = https://localhost</c> — sin esto,
/// <see cref="System.Net.CookieContainer"/> guarda la cookie pero nunca la reenvía sobre una
/// conexión http.
/// </summary>
public sealed class ComprasControllerTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Server=localhost,14330;Database=BingoCart;User Id=sa;" +
        "Password=BingoCart_Dev2026!;TrustServerCertificate=True;Encrypt=True;";

    private const string RedisConnectionString = "localhost:16379";

    private const string CookieCarritoName = "bingocart_carrito";

    private const string TelefonoValido = "+54 11 4444-5555";

    private const string PasswordValida = "Passw0rd!";

    private static readonly int[] MultiplicadoresCuit = { 5, 4, 3, 2, 7, 6, 5, 4, 3, 2 };

    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly WebApplicationFactory<Program> _factory;
    private readonly List<Guid> _organizadorIdsCreados = new();
    private readonly List<string> _mailsCreados = new();
    private readonly List<string> _clavesRedisABorrar = new();
    private ConnectionMultiplexer _redisConnectionMultiplexer = null!;

    public ComprasControllerTests()
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
                var compraCartones = await context.CompraCartones
                    .Join(context.Cartones, cc => cc.CartonId, c => c.Id, (cc, c) => new { cc, c.BingoId })
                    .Join(context.Bingos.Where(b => _organizadorIdsCreados.Contains(b.OrganizadorId)),
                        x => x.BingoId, b => b.Id, (x, b) => x.cc)
                    .ToListAsync();
                var compraIds = compraCartones.Select(cc => cc.CompraId).Distinct().ToList();
                context.CompraCartones.RemoveRange(compraCartones);
                var compras = await context.Compras.Where(c => compraIds.Contains(c.Id)).ToListAsync();
                context.Compras.RemoveRange(compras);
                await context.SaveChangesAsync();

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

    // Semilla aleatoria, mismo criterio que CompradoresControllerTests: evita colisión del índice
    // único de Cuit entre clases de test corriendo en paralelo.
    private static string NuevoCuitValido()
    {
        while (true)
        {
            var semilla = (uint)Guid.NewGuid().GetHashCode() % 89_999_999u;
            var cuerpo = "30" + (10_000_000 + semilla).ToString("D8");
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
                continue;
            }

            return cuerpo + digitoVerificador;
        }
    }

    private HttpClient NuevoClienteHttps() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

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

    private async Task<(Guid OrganizadorId, Guid BingoId, List<Guid> CartonIds)> SembrarOrganizadorConBingoYCartonesAsync(
        string nombreOrganizacion, int cantidadCartones, decimal costoPorCarton = 100m)
    {
        var ahoraUtc = DateTime.UtcNow;
        var organizadorId = Guid.NewGuid();
        var mail = $"test-compras-org-{organizadorId}@example.com";

        var organizador = new ApplicationUser
        {
            Id = organizadorId,
            UserName = mail,
            Email = mail,
            NombreOrganizacion = nombreOrganizacion,
            Cuit = NuevoCuitValido(),
            Telefono = TelefonoValido,
        };
        var bingo = Bingo.Crear("Bingo de prueba", ahoraUtc.AddDays(10), cantidadCartones, costoPorCarton, organizadorId, ahoraUtc);
        var cartones = NuevosCartones(bingo.Id, cantidadCartones);

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;
        await using var context = new AppDbContext(options);
        context.Users.Add(organizador);
        context.Bingos.Add(bingo);
        context.Cartones.AddRange(cartones);
        await context.SaveChangesAsync();

        _organizadorIdsCreados.Add(organizadorId);
        _mailsCreados.Add(mail);

        return (organizadorId, bingo.Id, cartones.Select(c => c.Id).ToList());
    }

    /// <summary>
    /// Registra y loguea un comprador real vía los endpoints HTTP de <c>CompradoresController</c>,
    /// devolviendo un cliente HTTPS ya con la cookie <c>bingocart_auth</c> (rol <c>Comprador</c>).
    /// </summary>
    private async Task<HttpClient> NuevoCompradorAutenticadoAsync()
    {
        var mail = $"test-compras-comprador-{Guid.NewGuid()}@example.com";
        _mailsCreados.Add(mail);

        var client = NuevoClienteHttps();
        var registro = await client.PostAsJsonAsync("/api/compradores/registro", new
        {
            apellido = "Gomez",
            nombre = "Ana",
            cuit = NuevoCuitValido(),
            mail,
            password = PasswordValida,
        });
        registro.EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync("/api/compradores/login", new { mail, password = PasswordValida });
        login.EnsureSuccessStatusCode();

        return client;
    }

    /// <summary>
    /// Registra un organizador real vía <c>POST /api/organizadores/registro</c> + login, devolviendo
    /// un cliente HTTPS autenticado con rol <c>Organizador</c> — necesario para el test que verifica
    /// que <c>[Authorize(Roles = "Comprador")]</c> distingue roles.
    /// </summary>
    private async Task<HttpClient> NuevoOrganizadorAutenticadoAsync()
    {
        var mail = $"test-compras-organizador-login-{Guid.NewGuid()}@example.com";
        _mailsCreados.Add(mail);

        var client = NuevoClienteHttps();
        var registro = await client.PostAsJsonAsync("/api/organizadores/registro", new
        {
            nombreOrganizacion = "Club Login Organizador",
            cuit = NuevoCuitValido(),
            mail,
            telefono = TelefonoValido,
            password = PasswordValida,
        });
        registro.EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync("/api/organizadores/login", new { mail, password = PasswordValida });
        login.EnsureSuccessStatusCode();

        return client;
    }

    private void RegistrarLimpiezaRedisDeCarrito(HttpResponseMessage responseConSetCookie, params Guid[] cartonIdsAgregados)
    {
        Assert.True(responseConSetCookie.Headers.TryGetValues("Set-Cookie", out var cookies));
        var cookie = cookies!.SingleOrDefault(c => c.StartsWith($"{CookieCarritoName}=", StringComparison.Ordinal));
        if (cookie is null)
        {
            return;
        }

        var valorConAtributos = cookie[(CookieCarritoName.Length + 1)..];
        var finDelValor = valorConAtributos.IndexOf(';');
        var valorCrudo = finDelValor >= 0 ? valorConAtributos[..finDelValor] : valorConAtributos;
        var sesionId = Uri.UnescapeDataString(valorCrudo);

        _clavesRedisABorrar.Add($"carrito:{sesionId}");
        foreach (var cartonId in cartonIdsAgregados)
        {
            _clavesRedisABorrar.Add($"reservado:carton:{cartonId}");
        }
    }

    [Fact]
    public async Task Confirmar_ConDosCartonesDeUnSoloOrganizador_Devuelve200ConUnaCompraYVaciaElCarrito()
    {
        var (_, _, cartonIds) = await SembrarOrganizadorConBingoYCartonesAsync("Club Compra Uno", 2, costoPorCarton: 100m);

        using var comprador = await NuevoCompradorAutenticadoAsync();
        var agregado1 = await comprador.PostAsync($"/api/carrito/cartones/{cartonIds[0]}", content: null);
        agregado1.EnsureSuccessStatusCode();
        (await comprador.PostAsync($"/api/carrito/cartones/{cartonIds[1]}", content: null)).EnsureSuccessStatusCode();
        RegistrarLimpiezaRedisDeCarrito(agregado1, cartonIds[0], cartonIds[1]);

        var confirmar = await comprador.PostAsJsonAsync("/api/compras/confirmar", new { medioPago = "Transferencia" });

        Assert.Equal(HttpStatusCode.OK, confirmar.StatusCode);
        var respuesta = await confirmar.Content.ReadFromJsonAsync<ConfirmarCompraResponseDto>(DeserializeOptions);
        Assert.NotNull(respuesta);
        Assert.Single(respuesta!.Compras);
        Assert.Equal(2, respuesta.Compras[0].CantidadCartones);
        Assert.Equal(200m, respuesta.Compras[0].MontoTotal);

        var ver = await comprador.GetAsync("/api/carrito");
        var carrito = await ver.Content.ReadFromJsonAsync<CarritoResponseDto>(DeserializeOptions);
        Assert.NotNull(carrito);
        Assert.Empty(carrito!.Items);
    }

    [Fact]
    public async Task Confirmar_ConCartonesDeDosOrganizadoresDistintos_Devuelve200ConDosCompras()
    {
        var (_, _, cartonIdsUno) = await SembrarOrganizadorConBingoYCartonesAsync("Club Compra Dos A", 1);
        var (_, _, cartonIdsDos) = await SembrarOrganizadorConBingoYCartonesAsync("Club Compra Dos B", 1);

        using var comprador = await NuevoCompradorAutenticadoAsync();
        var agregado1 = await comprador.PostAsync($"/api/carrito/cartones/{cartonIdsUno[0]}", content: null);
        agregado1.EnsureSuccessStatusCode();
        (await comprador.PostAsync($"/api/carrito/cartones/{cartonIdsDos[0]}", content: null)).EnsureSuccessStatusCode();
        RegistrarLimpiezaRedisDeCarrito(agregado1, cartonIdsUno[0], cartonIdsDos[0]);

        var confirmar = await comprador.PostAsJsonAsync("/api/compras/confirmar", new { medioPago = "Efectivo" });

        Assert.Equal(HttpStatusCode.OK, confirmar.StatusCode);
        var respuesta = await confirmar.Content.ReadFromJsonAsync<ConfirmarCompraResponseDto>(DeserializeOptions);
        Assert.NotNull(respuesta);
        Assert.Equal(2, respuesta!.Compras.Count);
        Assert.All(respuesta.Compras, c => Assert.Equal(1, c.CantidadCartones));
    }

    [Fact]
    public async Task Confirmar_ConCarritoVacio_Devuelve400CarritoVacio()
    {
        using var comprador = await NuevoCompradorAutenticadoAsync();

        var confirmar = await comprador.PostAsJsonAsync("/api/compras/confirmar", new { medioPago = "Efectivo" });

        Assert.Equal(HttpStatusCode.BadRequest, confirmar.StatusCode);
        var error = await confirmar.Content.ReadFromJsonAsync<ErrorResponseDto>(DeserializeOptions);
        Assert.NotNull(error);
        Assert.Equal("CarritoVacio", error!.Error);
    }

    [Fact]
    public async Task Confirmar_ConUnaReservaDeRedisBorradaDirectamente_Devuelve409ReservaCarritoInvalidaConElCartonAfectado()
    {
        var (_, _, cartonIds) = await SembrarOrganizadorConBingoYCartonesAsync("Club Compra Vencida", 1);

        using var comprador = await NuevoCompradorAutenticadoAsync();
        var agregado = await comprador.PostAsync($"/api/carrito/cartones/{cartonIds[0]}", content: null);
        agregado.EnsureSuccessStatusCode();
        RegistrarLimpiezaRedisDeCarrito(agregado, cartonIds[0]);

        // Simula la expiración de la reserva sin esperar el TTL real (mismo mecanismo que
        // CarritoRepositoryTests): borra directamente la clave reservado:carton:{cartonId}, dejando
        // el carrito ("carrito:{sesionId}") intacto — la revalidación debe detectar la inconsistencia.
        var db = _redisConnectionMultiplexer.GetDatabase();
        await db.KeyDeleteAsync($"reservado:carton:{cartonIds[0]}");

        var confirmar = await comprador.PostAsJsonAsync("/api/compras/confirmar", new { medioPago = "Efectivo" });

        Assert.Equal(HttpStatusCode.Conflict, confirmar.StatusCode);
        var body = await confirmar.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal("ReservaCarritoInvalida", json.RootElement.GetProperty("error").GetString());
        var cartonIdsInvalidos = json.RootElement.GetProperty("cartonIdsInvalidos")
            .EnumerateArray().Select(e => e.GetGuid()).ToList();
        Assert.Contains(cartonIds[0], cartonIdsInvalidos);
    }

    [Fact]
    public async Task Confirmar_SinAutenticacion_Devuelve401()
    {
        using var client = NuevoClienteHttps();

        var confirmar = await client.PostAsJsonAsync("/api/compras/confirmar", new { medioPago = "Efectivo" });

        Assert.Equal(HttpStatusCode.Unauthorized, confirmar.StatusCode);
    }

    [Fact]
    public async Task Confirmar_AutenticadoComoOrganizador_Devuelve403()
    {
        using var organizador = await NuevoOrganizadorAutenticadoAsync();

        var confirmar = await organizador.PostAsJsonAsync("/api/compras/confirmar", new { medioPago = "Efectivo" });

        Assert.Equal(HttpStatusCode.Forbidden, confirmar.StatusCode);
    }

    [Fact]
    public async Task Confirmar_ConDosCompradoresDeCarritosNoSolapadosEnParalelo_AmbasConfirmacionesTerminanExitosas()
    {
        var (_, _, cartonIdsUno) = await SembrarOrganizadorConBingoYCartonesAsync("Club Concurrencia A", 1);
        var (_, _, cartonIdsDos) = await SembrarOrganizadorConBingoYCartonesAsync("Club Concurrencia B", 1);

        using var compradorUno = await NuevoCompradorAutenticadoAsync();
        using var compradorDos = await NuevoCompradorAutenticadoAsync();

        var agregadoUno = await compradorUno.PostAsync($"/api/carrito/cartones/{cartonIdsUno[0]}", content: null);
        agregadoUno.EnsureSuccessStatusCode();
        RegistrarLimpiezaRedisDeCarrito(agregadoUno, cartonIdsUno[0]);

        var agregadoDos = await compradorDos.PostAsync($"/api/carrito/cartones/{cartonIdsDos[0]}", content: null);
        agregadoDos.EnsureSuccessStatusCode();
        RegistrarLimpiezaRedisDeCarrito(agregadoDos, cartonIdsDos[0]);

        var confirmarUnoTask = compradorUno.PostAsJsonAsync("/api/compras/confirmar", new { medioPago = "Efectivo" });
        var confirmarDosTask = compradorDos.PostAsJsonAsync("/api/compras/confirmar", new { medioPago = "Transferencia" });
        await Task.WhenAll(confirmarUnoTask, confirmarDosTask);

        Assert.Equal(HttpStatusCode.OK, confirmarUnoTask.Result.StatusCode);
        Assert.Equal(HttpStatusCode.OK, confirmarDosTask.Result.StatusCode);
    }

    [Fact]
    public async Task Confirmar_Con10RequestsPreviasEnLaVentana_Request11Devuelve429()
    {
        using var comprador = await NuevoCompradorAutenticadoAsync();

        // Todos fallan con 400 (carrito vacío, a propósito para no depender de bingos/cartones
        // reales), pero cuentan igual para el rate limit particionado por el claim NameIdentifier
        // del JWT del comprador (política "compras", 10 req/5 min).
        for (var intento = 1; intento <= 10; intento++)
        {
            var respuesta = await comprador.PostAsJsonAsync("/api/compras/confirmar", new { medioPago = "Efectivo" });
            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        }

        var request11 = await comprador.PostAsJsonAsync("/api/compras/confirmar", new { medioPago = "Efectivo" });

        Assert.Equal(HttpStatusCode.TooManyRequests, request11.StatusCode);
    }

    // Regresión: corrective loop de daw-arch-auditor sobre CODE Block 3, ronda 2. La política
    // "compras" particiona por el claim NameIdentifier del JWT — si UseRateLimiter() corre antes
    // que UseAuthentication()/UseAuthorization() en Program.cs, HttpContext.User todavía no está
    // poblado en ese punto del pipeline, así que ese claim siempre resuelve null y TODOS los
    // compradores caen en el mismo bucket "unknown", en vez de un bucket por comprador. El test
    // anterior (un solo comprador) no puede detectar este bug porque nunca compara dos usuarios
    // distintos. Acá, comprador A agota su límite (10 req/5 min) y comprador B — un JWT
    // independiente, con su propia cookie en un HttpClient separado — hace su primera request
    // jamás a este endpoint: si el particionado fuera por usuario (correcto), B no debería verse
    // afectado por el consumo de A.
    [Fact]
    public async Task Confirmar_ConCompradorAAgotandoSuLimite_CompradorBNoSeVeAfectado()
    {
        using var compradorA = await NuevoCompradorAutenticadoAsync();
        using var compradorB = await NuevoCompradorAutenticadoAsync();

        for (var intento = 1; intento <= 10; intento++)
        {
            var respuesta = await compradorA.PostAsJsonAsync("/api/compras/confirmar", new { medioPago = "Efectivo" });
            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        }

        var request11DeA = await compradorA.PostAsJsonAsync("/api/compras/confirmar", new { medioPago = "Efectivo" });
        Assert.Equal(HttpStatusCode.TooManyRequests, request11DeA.StatusCode);

        var primeraRequestDeB = await compradorB.PostAsJsonAsync("/api/compras/confirmar", new { medioPago = "Efectivo" });

        Assert.Equal(HttpStatusCode.BadRequest, primeraRequestDeB.StatusCode);
        var error = await primeraRequestDeB.Content.ReadFromJsonAsync<ErrorResponseDto>(DeserializeOptions);
        Assert.NotNull(error);
        Assert.Equal("CarritoVacio", error!.Error);
    }

    private sealed record ErrorResponseDto(string Error, string Message);

    private sealed record ItemCarritoResponseDto(Guid CartonId, string NombreOrganizacion, string NombreEvento, decimal PrecioUnitario);

    private sealed record CarritoResponseDto(List<ItemCarritoResponseDto> Items, int CantidadTotal, decimal MontoTotal);

    private sealed record CompraCreadaDto(Guid CompraId, Guid OrganizadorId, string NombreOrganizacion, int CantidadCartones, decimal MontoTotal);

    private sealed record ConfirmarCompraResponseDto(List<CompraCreadaDto> Compras);
}
