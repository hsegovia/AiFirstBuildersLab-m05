using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BingoCart.Domain.Bingos;
using BingoCart.Domain.Compras;
using BingoCart.Infrastructure.Auth;
using BingoCart.Infrastructure.Compras;
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
    // Spec FEAT-009c, Block 3: compras sembradas directo contra la base para los tests de
    // ConfirmarPago/Cancelar/ListarMias — algunas (compra ajena, listado) no están atadas a un
    // Bingo/Carton real, así que la limpieza basada en `_organizadorIdsCreados` (JOIN a
    // CompraCartones->Cartones->Bingos) no las alcanza. Se borran por Id, con cascade delete de EF
    // Core hacia sus CompraCartones (Rule #0 de testing.instructions.md).
    private readonly List<Guid> _compraIdsCreadas = new();
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
        if (_compraIdsCreadas.Count > 0)
        {
            var optionsCompras = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(ConnectionString)
                .Options;
            await using var contextCompras = new AppDbContext(optionsCompras);

            var comprasDirectas = await contextCompras.Compras
                .Where(c => _compraIdsCreadas.Contains(c.Id))
                .ToListAsync();
            contextCompras.Compras.RemoveRange(comprasDirectas);
            await contextCompras.SaveChangesAsync();
        }

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
            cartones.Add(Carton.Crear(bingoId, numeros, numeroCorrelativo: i + 1));
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

    /// <summary>
    /// Mismo flujo que <see cref="NuevoOrganizadorAutenticadoAsync"/>, pero además devuelve el
    /// <c>Id</c> del organizador registrado (spec FEAT-009c, Block 3) — necesario para sembrar
    /// compras/bingos propios y poder ejercitar el ownership check de
    /// <c>ConfirmarPagoAsync</c>/<c>CancelarAsync</c> (FR-08/AC-08) contra el MISMO organizador
    /// autenticado.
    /// </summary>
    private async Task<(HttpClient Client, Guid OrganizadorId)> NuevoOrganizadorAutenticadoConIdAsync()
    {
        var mail = $"test-compras-org-accion-{Guid.NewGuid()}@example.com";
        _mailsCreados.Add(mail);

        var client = NuevoClienteHttps();
        var registro = await client.PostAsJsonAsync("/api/organizadores/registro", new
        {
            nombreOrganizacion = "Club Accion Organizador",
            cuit = NuevoCuitValido(),
            mail,
            telefono = TelefonoValido,
            password = PasswordValida,
        });
        registro.EnsureSuccessStatusCode();
        var registroBody = await registro.Content.ReadFromJsonAsync<RegistroOrganizadorDto>(DeserializeOptions);

        var login = await client.PostAsJsonAsync("/api/organizadores/login", new { mail, password = PasswordValida });
        login.EnsureSuccessStatusCode();

        return (client, registroBody!.Id);
    }

    /// <summary>
    /// Siembra un Bingo activo con un único Carton real para <paramref name="organizadorId"/> (spec
    /// FEAT-009c, Block 3) — necesario para el test end-to-end de <c>Cancelar</c>, que verifica que
    /// el cartón reaparece en <c>GET /api/cartones/organizador/{id}</c>. Registra
    /// <paramref name="organizadorId"/> en <see cref="_organizadorIdsCreados"/>: reutiliza la
    /// limpieza ya existente basada en ese Bingo (JOIN Cartones/CompraCartones).
    /// </summary>
    private async Task<(Guid BingoId, Guid CartonId)> SembrarBingoYCartonParaOrganizadorAsync(Guid organizadorId)
    {
        var ahoraUtc = DateTime.UtcNow;
        var bingo = Bingo.Crear("Bingo cancelacion e2e", ahoraUtc.AddDays(10), 1, 100m, organizadorId, ahoraUtc);
        var carton = Carton.Crear(bingo.Id, new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, numeroCorrelativo: 1);

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;
        await using var context = new AppDbContext(options);
        context.Bingos.Add(bingo);
        context.Cartones.Add(carton);
        await context.SaveChangesAsync();

        _organizadorIdsCreados.Add(organizadorId);

        return (bingo.Id, carton.Id);
    }

    /// <summary>
    /// Siembra una <see cref="Compra"/> directo contra la base (spec FEAT-009c, Block 3), en el
    /// estado <paramref name="estadoDeseado"/> (transicionada vía Domain, nunca seteada a mano).
    /// <paramref name="cartonId"/> no necesita ser un Carton real (sin FK física, ver
    /// <c>AppDbContext</c>) salvo que el test también valide descubrimiento. Registra el Id en
    /// <see cref="_compraIdsCreadas"/> para limpieza (Rule #0).
    /// </summary>
    private async Task<Compra> SembrarCompraAsync(
        Guid organizadorId,
        Guid compradorId,
        Guid cartonId,
        decimal monto,
        EstadoCompra estadoDeseado = EstadoCompra.PendienteConfirmacionPago)
    {
        var compra = Compra.Crear(
            organizadorId,
            compradorId,
            Guid.NewGuid(),
            new[] { new ItemCompra(cartonId, monto) },
            MedioPago.Efectivo,
            DateTime.UtcNow);

        if (estadoDeseado == EstadoCompra.Confirmado)
        {
            compra.ConfirmarPago();
        }
        else if (estadoDeseado == EstadoCompra.Cancelado)
        {
            compra.Cancelar();
        }

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;
        await using var context = new AppDbContext(options);
        context.Compras.Add(compra);
        context.CompraCartones.Add(new CompraCarton { CompraId = compra.Id, CartonId = cartonId, PrecioUnitario = monto });
        await context.SaveChangesAsync();

        _compraIdsCreadas.Add(compra.Id);

        return compra;
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

    // Spec FEAT-009c, Block 3: ConfirmarPago/Cancelar/ListarMias — los 3 endpoints nuevos del
    // organizador. `organizadorId` viene EXCLUSIVAMENTE del claim JWT del cliente autenticado
    // (NFR-03), nunca de un parámetro que estos tests puedan falsear.

    [Fact]
    public async Task ConfirmarPago_ConCompraPropiaPendiente_Devuelve200YEstadoConfirmado()
    {
        var (organizador, organizadorId) = await NuevoOrganizadorAutenticadoConIdAsync();
        var compra = await SembrarCompraAsync(organizadorId, Guid.NewGuid(), Guid.NewGuid(), 100m);

        var confirmar = await organizador.PatchAsync($"/api/compras/{compra.Id}/confirmar-pago", content: null);

        Assert.Equal(HttpStatusCode.OK, confirmar.StatusCode);
        var body = await confirmar.Content.ReadFromJsonAsync<CompraResumenDto>(DeserializeOptions);
        Assert.NotNull(body);
        Assert.Equal(compra.Id, body!.CompraId);
        Assert.Equal("Confirmado", body.Estado);
    }

    [Fact]
    public async Task ConfirmarPago_ConCompraAjena_Devuelve404()
    {
        var (organizador, _) = await NuevoOrganizadorAutenticadoConIdAsync();
        var compraAjena = await SembrarCompraAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 100m);

        var confirmar = await organizador.PatchAsync($"/api/compras/{compraAjena.Id}/confirmar-pago", content: null);

        Assert.Equal(HttpStatusCode.NotFound, confirmar.StatusCode);
        var error = await confirmar.Content.ReadFromJsonAsync<ErrorResponseDto>(DeserializeOptions);
        Assert.NotNull(error);
        Assert.Equal("CompraNoEncontrada", error!.Error);
    }

    [Fact]
    public async Task ConfirmarPago_ConCompraYaConfirmada_Devuelve409()
    {
        var (organizador, organizadorId) = await NuevoOrganizadorAutenticadoConIdAsync();
        var compra = await SembrarCompraAsync(organizadorId, Guid.NewGuid(), Guid.NewGuid(), 100m, EstadoCompra.Confirmado);

        var confirmar = await organizador.PatchAsync($"/api/compras/{compra.Id}/confirmar-pago", content: null);

        Assert.Equal(HttpStatusCode.Conflict, confirmar.StatusCode);
        var error = await confirmar.Content.ReadFromJsonAsync<ErrorResponseDto>(DeserializeOptions);
        Assert.NotNull(error);
        Assert.Equal("EstadoInvalido", error!.Error);
    }

    // End-to-end contra la base real (spec, Required tests): valida AC-03/AC-05 — cancelar libera
    // el cartón, que vuelve a aparecer en GET /api/cartones/organizador/{id} (descubrimiento
    // acotado, determinístico con un único cartón — a diferencia del endpoint global, que muestrea
    // al azar entre TODOS los cartones de la base compartida de test).
    [Fact]
    public async Task Cancelar_ConCompraPropiaPendiente_Devuelve200YLiberaCartones()
    {
        var (organizador, organizadorId) = await NuevoOrganizadorAutenticadoConIdAsync();
        var (_, cartonId) = await SembrarBingoYCartonParaOrganizadorAsync(organizadorId);
        var compra = await SembrarCompraAsync(organizadorId, Guid.NewGuid(), cartonId, 100m);

        var antes = await organizador.GetAsync($"/api/cartones/organizador/{organizadorId}");
        var cartonesAntes = await antes.Content.ReadFromJsonAsync<List<CartonDescubiertoDto>>(DeserializeOptions);
        Assert.DoesNotContain(cartonesAntes!, c => c.Id == cartonId);

        var cancelar = await organizador.PatchAsync($"/api/compras/{compra.Id}/cancelar", content: null);

        Assert.Equal(HttpStatusCode.OK, cancelar.StatusCode);
        var body = await cancelar.Content.ReadFromJsonAsync<CompraResumenDto>(DeserializeOptions);
        Assert.NotNull(body);
        Assert.Equal("Cancelado", body!.Estado);

        var despues = await organizador.GetAsync($"/api/cartones/organizador/{organizadorId}");
        var cartonesDespues = await despues.Content.ReadFromJsonAsync<List<CartonDescubiertoDto>>(DeserializeOptions);
        Assert.Contains(cartonesDespues!, c => c.Id == cartonId);
    }

    [Fact]
    public async Task Cancelar_SinRolOrganizador_Devuelve403()
    {
        using var comprador = await NuevoCompradorAutenticadoAsync();

        var cancelar = await comprador.PatchAsync($"/api/compras/{Guid.NewGuid()}/cancelar", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, cancelar.StatusCode);
    }

    [Fact]
    public async Task ListarMias_ConDosPaginas_RespetaPageSize()
    {
        var (organizador, organizadorId) = await NuevoOrganizadorAutenticadoConIdAsync();
        var compraUno = await SembrarCompraAsync(organizadorId, Guid.NewGuid(), Guid.NewGuid(), 100m);
        var compraDos = await SembrarCompraAsync(organizadorId, Guid.NewGuid(), Guid.NewGuid(), 150m);

        var primeraPagina = await organizador.GetAsync("/api/compras/mias?page=1&pageSize=1");
        var segundaPagina = await organizador.GetAsync("/api/compras/mias?page=2&pageSize=1");

        Assert.Equal(HttpStatusCode.OK, primeraPagina.StatusCode);
        var respuestaUno = await primeraPagina.Content.ReadFromJsonAsync<CompraListadoDto>(DeserializeOptions);
        Assert.NotNull(respuestaUno);
        Assert.Single(respuestaUno!.Items);
        Assert.Equal(2, respuestaUno.Total);
        Assert.Equal(2, respuestaUno.TotalPaginas);
        Assert.Equal(1, respuestaUno.PageSize);

        Assert.Equal(HttpStatusCode.OK, segundaPagina.StatusCode);
        var respuestaDos = await segundaPagina.Content.ReadFromJsonAsync<CompraListadoDto>(DeserializeOptions);
        Assert.NotNull(respuestaDos);
        Assert.Single(respuestaDos!.Items);

        var idsDevueltos = new[] { respuestaUno.Items[0].CompraId, respuestaDos!.Items[0].CompraId };
        Assert.Contains(compraUno.Id, idsDevueltos);
        Assert.Contains(compraDos.Id, idsDevueltos);
    }

    // Spec FEAT-009d, Block 2 (FR-12): la respuesta de POST /api/compras/confirmar es una de las
    // superficies que FR-12 nombra, así que devuelve los cartones de cada compra creada con su
    // correlativo. Se piden dos cartones NO contiguos del mismo bingo para que un mapeo que
    // devolviera siempre el primer correlativo, o el índice dentro del carrito, no pueda pasar.
    [Fact]
    public async Task Confirmar_ConDosCartones_DevuelveCadaCartonDeLaCompraConSuNumeroCorrelativo()
    {
        var (_, _, cartonIds) = await SembrarOrganizadorConBingoYCartonesAsync(
            "Club Correlativo Confirmacion", 3, costoPorCarton: 50m);

        using var comprador = await NuevoCompradorAutenticadoAsync();
        var agregado = await comprador.PostAsync($"/api/carrito/cartones/{cartonIds[0]}", content: null);
        agregado.EnsureSuccessStatusCode();
        (await comprador.PostAsync($"/api/carrito/cartones/{cartonIds[2]}", content: null)).EnsureSuccessStatusCode();
        RegistrarLimpiezaRedisDeCarrito(agregado, cartonIds[0], cartonIds[2]);

        var confirmar = await comprador.PostAsJsonAsync("/api/compras/confirmar", new { medioPago = "Efectivo" });

        Assert.Equal(HttpStatusCode.OK, confirmar.StatusCode);
        var respuesta = await confirmar.Content.ReadFromJsonAsync<ConfirmarCompraResponseDto>(DeserializeOptions);
        Assert.NotNull(respuesta);
        var compra = Assert.Single(respuesta!.Compras);
        Assert.NotNull(compra.Cartones);
        Assert.Equal(2, compra.Cartones!.Count);
        Assert.Equal(1, Assert.Single(compra.Cartones, c => c.CartonId == cartonIds[0]).NumeroCorrelativo);
        Assert.Equal(3, Assert.Single(compra.Cartones, c => c.CartonId == cartonIds[2]).NumeroCorrelativo);
        // El campo nuevo es aditivo: lo que la respuesta ya devolvía sigue intacto.
        Assert.Equal(2, compra.CantidadCartones);
        Assert.Equal(100m, compra.MontoTotal);
    }

    // Spec FEAT-009d, Block 2 — AC-14 sobre las superficies que existen al cerrar este bloque (la
    // de "mis cartones" llega en Block 3 y AC-14 se valida entero allí): un mismo cartón muestra el
    // mismo correlativo en el descubrimiento, en el carrito, en la respuesta de confirmación de
    // compra y en el mail. El mail es la única de las cuatro que no tiene endpoint HTTP: se verifica
    // sobre los datos que EnvioMailService toma para armarlo, incluido el PDF adjunto.
    [Fact]
    public async Task Correlativo_DelMismoCarton_EsElMismoEnDescubrimientoEnElCarritoYEnElMailDeConfirmacion()
    {
        var (organizadorId, _, cartonIds) = await SembrarOrganizadorConBingoYCartonesAsync(
            "Club Correlativo Consistencia", 1, costoPorCarton: 100m);
        var cartonId = cartonIds[0];

        using var comprador = await NuevoCompradorAutenticadoAsync();

        var descubrimiento = await comprador.GetAsync($"/api/cartones/organizador/{organizadorId}");
        descubrimiento.EnsureSuccessStatusCode();
        var cartonesDescubiertos = await descubrimiento.Content.ReadFromJsonAsync<List<CartonDescubiertoDto>>(DeserializeOptions);
        Assert.NotNull(cartonesDescubiertos);
        var correlativoEnDescubrimiento = Assert.Single(cartonesDescubiertos!, c => c.Id == cartonId).NumeroCorrelativo;

        var agregado = await comprador.PostAsync($"/api/carrito/cartones/{cartonId}", content: null);
        agregado.EnsureSuccessStatusCode();
        RegistrarLimpiezaRedisDeCarrito(agregado, cartonId);
        var ver = await comprador.GetAsync("/api/carrito");
        var carrito = await ver.Content.ReadFromJsonAsync<CarritoResponseDto>(DeserializeOptions);
        Assert.NotNull(carrito);
        var correlativoEnCarrito = Assert.Single(carrito!.Items).NumeroCorrelativo;

        var confirmar = await comprador.PostAsJsonAsync("/api/compras/confirmar", new { medioPago = "Efectivo" });
        Assert.Equal(HttpStatusCode.OK, confirmar.StatusCode);
        var respuesta = await confirmar.Content.ReadFromJsonAsync<ConfirmarCompraResponseDto>(DeserializeOptions);
        Assert.NotNull(respuesta);
        var compraCreada = Assert.Single(respuesta!.Compras);
        var compraId = compraCreada.CompraId;
        var correlativoEnConfirmacion = Assert.Single(compraCreada.Cartones).NumeroCorrelativo;

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;
        await using var context = new AppDbContext(options);
        var confirmacionId = (await context.Compras.AsNoTracking().SingleAsync(c => c.Id == compraId)).ConfirmacionId;
        var datosMail = await new EnvioMailRepository(context).ObtenerDatosParaEnviarAsync(confirmacionId);
        Assert.NotNull(datosMail);
        var correlativoEnMail = Assert.Single(Assert.Single(datosMail!.Compras).Cartones).NumeroCorrelativo;

        Assert.Equal(1, correlativoEnDescubrimiento);
        Assert.Equal(correlativoEnDescubrimiento, correlativoEnCarrito);
        Assert.Equal(correlativoEnDescubrimiento, correlativoEnConfirmacion);
        Assert.Equal(correlativoEnDescubrimiento, correlativoEnMail);
    }

    private sealed record ErrorResponseDto(string Error, string Message);

    private sealed record ItemCarritoResponseDto(
        Guid CartonId, int NumeroCorrelativo, string NombreOrganizacion, string NombreEvento, decimal PrecioUnitario);

    private sealed record CarritoResponseDto(List<ItemCarritoResponseDto> Items, int CantidadTotal, decimal MontoTotal);

    private sealed record CompraCreadaDto(
        Guid CompraId,
        Guid OrganizadorId,
        string NombreOrganizacion,
        int CantidadCartones,
        decimal MontoTotal,
        List<CartonCompradoDto> Cartones);

    private sealed record CartonCompradoDto(Guid CartonId, int NumeroCorrelativo);

    private sealed record ConfirmarCompraResponseDto(List<CompraCreadaDto> Compras);

    private sealed record RegistroOrganizadorDto(Guid Id, string NombreOrganizacion, string Mail);

    private sealed record CompraResumenDto(Guid CompraId, string Estado, decimal MontoTotal, DateTime FechaCreacionUtc);

    private sealed record CompraListadoDto(List<CompraResumenDto> Items, int Total, int TotalPaginas, int Page, int PageSize);

    private sealed record CartonDescubiertoDto(
        Guid Id, int NumeroCorrelativo, string NombreOrganizacion, string NombreEvento, DateTime FechaSorteoUtc,
        decimal CostoPorCarton, List<int> Numeros);
}
