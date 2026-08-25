using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BingoCart.Domain.Bingos;
using BingoCart.Domain.Compras;
using BingoCart.Infrastructure.Compras;
using BingoCart.Infrastructure.Data;
using BingoCart.Infrastructure.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace BingoCart.Api.Tests.Controllers;

/// <summary>
/// Tests de integración de <c>MisCartonesController</c> (spec FEAT-009d, Block 3) contra el stack
/// completo levantado en memoria (<see cref="WebApplicationFactory{T}"/>), el SQL Server real
/// dockerizado (puerto 14330) y el Redis real dockerizado (puerto 16379) — mismo patrón que
/// <c>ComprasControllerTests</c>: organizador+bingo+cartones sembrados directo contra
/// <see cref="AppDbContext"/>, comprador registrado/logueado vía los endpoints HTTP reales de
/// <c>CompradoresController</c>. Ambas cookies (<c>bingocart_auth</c>/<c>bingocart_carrito</c>) son
/// <c>Secure</c>, así que todo cliente usa <c>BaseAddress = https://localhost</c>.
/// </summary>
public sealed class MisCartonesControllerTests : IAsyncLifetime
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
    private readonly List<Guid> _compraIdsCreadas = new();
    private readonly List<Guid> _envioMailIdsCreados = new();
    private ConnectionMultiplexer _redisConnectionMultiplexer = null!;

    public MisCartonesControllerTests()
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
        if (_envioMailIdsCreados.Count > 0)
        {
            var optionsMail = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;
            await using var contextMail = new AppDbContext(optionsMail);
            var envios = await contextMail.EnviosMail.Where(e => _envioMailIdsCreados.Contains(e.Id)).ToListAsync();
            contextMail.EnviosMail.RemoveRange(envios);
            await contextMail.SaveChangesAsync();
        }

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

    // Semilla aleatoria, mismo criterio que ComprasControllerTests: evita colisión del índice único
    // de Cuit entre clases de test corriendo en paralelo.
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
        var mail = $"test-mis-cartones-org-{organizadorId}@example.com";

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
    /// devolviendo un cliente HTTPS ya con la cookie <c>bingocart_auth</c> (rol <c>Comprador</c>) y
    /// el <c>Id</c> del comprador registrado — necesario para sembrar compras propias directamente
    /// contra la base con el mismo <c>CompradorId</c> que el JWT lleva en su claim.
    /// </summary>
    private async Task<(HttpClient Client, Guid CompradorId)> NuevoCompradorAutenticadoConIdAsync()
    {
        var mail = $"test-mis-cartones-comprador-{Guid.NewGuid()}@example.com";
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
        var registroBody = await registro.Content.ReadFromJsonAsync<RegistroCompradorDto>(DeserializeOptions);

        var login = await client.PostAsJsonAsync("/api/compradores/login", new { mail, password = PasswordValida });
        login.EnsureSuccessStatusCode();

        return (client, registroBody!.Id);
    }

    /// <summary>
    /// Registra un organizador real vía <c>POST /api/organizadores/registro</c> + login — necesario
    /// para el test que verifica que <c>[Authorize(Roles = "Comprador")]</c> distingue roles.
    /// </summary>
    private async Task<HttpClient> NuevoOrganizadorAutenticadoAsync()
    {
        var mail = $"test-mis-cartones-organizador-login-{Guid.NewGuid()}@example.com";
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
    /// Siembra una <see cref="Compra"/> directo contra la base, referenciando un <paramref name="cartonId"/>
    /// REAL (a diferencia de <c>ComprasControllerTests.SembrarCompraAsync</c>, acá el join de 4 tablas
    /// de <c>ListarCartonesDelCompradorAsync</c> exige que el cartón exista de verdad). Registra el Id
    /// en <see cref="_compraIdsCreadas"/> para limpieza.
    /// </summary>
    private async Task<Compra> SembrarCompraConCartonAsync(
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
    public async Task Listar_ConCartonesAdquiridos_DevuelveTodosLosCamposEsperados()
    {
        var (organizadorId, _, cartonIds) = await SembrarOrganizadorConBingoYCartonesAsync(
            "Club Mis Cartones Uno", 1, costoPorCarton: 100m);
        var (comprador, compradorId) = await NuevoCompradorAutenticadoConIdAsync();
        var compra = await SembrarCompraConCartonAsync(
            organizadorId, compradorId, cartonIds[0], 100m, EstadoCompra.Confirmado);

        var respuesta = await comprador.GetAsync("/api/compradores/mis-cartones");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var body = await respuesta.Content.ReadFromJsonAsync<MisCartonesResponseDto>(DeserializeOptions);
        Assert.NotNull(body);
        var item = Assert.Single(body!.Items);
        Assert.Equal(cartonIds[0], item.CartonId);
        Assert.Equal(1, item.NumeroCorrelativo);
        Assert.Equal(10, item.Numeros.Count);
        Assert.Equal("Bingo de prueba", item.NombreBingo);
        Assert.Equal("Club Mis Cartones Uno", item.NombreOrganizacion);
        Assert.Equal("Confirmado", item.EstadoCompra);
        Assert.Equal(compra.Id, item.CompraId);
    }

    [Fact]
    public async Task Listar_ConComprasEnDistintosEstados_LasIncluyeTodasConSuEstado()
    {
        var (organizadorId, _, cartonIds) = await SembrarOrganizadorConBingoYCartonesAsync(
            "Club Mis Cartones Estados", 3, costoPorCarton: 100m);
        var (comprador, compradorId) = await NuevoCompradorAutenticadoConIdAsync();

        await SembrarCompraConCartonAsync(organizadorId, compradorId, cartonIds[0], 100m, EstadoCompra.PendienteConfirmacionPago);
        await SembrarCompraConCartonAsync(organizadorId, compradorId, cartonIds[1], 100m, EstadoCompra.Confirmado);
        await SembrarCompraConCartonAsync(organizadorId, compradorId, cartonIds[2], 100m, EstadoCompra.Cancelado);

        var respuesta = await comprador.GetAsync("/api/compradores/mis-cartones?pageSize=50");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var body = await respuesta.Content.ReadFromJsonAsync<MisCartonesResponseDto>(DeserializeOptions);
        Assert.NotNull(body);
        Assert.Equal(3, body!.Items.Count);
        // FR-02/AC-02/A-01: la compra Cancelada SÍ aparece, con su propio estado — el listado del
        // comprador no replica el filtro de disponibilidad de venta de FEAT-009c.
        Assert.Contains(body.Items, i => i.CartonId == cartonIds[0] && i.EstadoCompra == "PendienteConfirmacionPago");
        Assert.Contains(body.Items, i => i.CartonId == cartonIds[1] && i.EstadoCompra == "Confirmado");
        Assert.Contains(body.Items, i => i.CartonId == cartonIds[2] && i.EstadoCompra == "Cancelado");
    }

    [Fact]
    public async Task Listar_NoDevuelveCartonesDeOtroComprador()
    {
        var (organizadorId, _, cartonIds) = await SembrarOrganizadorConBingoYCartonesAsync(
            "Club Mis Cartones Ajeno", 2, costoPorCarton: 100m);
        var (compradorA, compradorAId) = await NuevoCompradorAutenticadoConIdAsync();
        var otroCompradorId = Guid.NewGuid();

        await SembrarCompraConCartonAsync(organizadorId, compradorAId, cartonIds[0], 100m, EstadoCompra.Confirmado);
        await SembrarCompraConCartonAsync(organizadorId, otroCompradorId, cartonIds[1], 100m, EstadoCompra.Confirmado);

        var respuesta = await compradorA.GetAsync("/api/compradores/mis-cartones");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var body = await respuesta.Content.ReadFromJsonAsync<MisCartonesResponseDto>(DeserializeOptions);
        Assert.NotNull(body);
        var item = Assert.Single(body!.Items);
        Assert.Equal(cartonIds[0], item.CartonId);
        Assert.DoesNotContain(body.Items, i => i.CartonId == cartonIds[1]);
    }

    [Fact]
    public async Task Listar_ConEnvioDeMailFallido_DevuelveElListadoCompleto()
    {
        var (organizadorId, _, cartonIds) = await SembrarOrganizadorConBingoYCartonesAsync(
            "Club Mis Cartones Mail Fallido", 1, costoPorCarton: 100m);
        var (comprador, compradorId) = await NuevoCompradorAutenticadoConIdAsync();
        var compra = await SembrarCompraConCartonAsync(
            organizadorId, compradorId, cartonIds[0], 100m, EstadoCompra.Confirmado);

        var ahoraUtc = DateTime.UtcNow;
        var envioFallido = EnvioMail.CrearConfirmacion(compra.ConfirmacionId, compradorId, ahoraUtc);
        envioFallido.RegistrarIntentoFallido(ahoraUtc);
        envioFallido.RegistrarIntentoFallido(ahoraUtc);
        envioFallido.RegistrarIntentoFallido(ahoraUtc);
        Assert.Equal(EstadoEnvioMail.Fallido, envioFallido.Estado);

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;
        await using (var context = new AppDbContext(options))
        {
            context.EnviosMail.Add(envioFallido);
            await context.SaveChangesAsync();
        }
        _envioMailIdsCreados.Add(envioFallido.Id);

        var respuesta = await comprador.GetAsync("/api/compradores/mis-cartones");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var body = await respuesta.Content.ReadFromJsonAsync<MisCartonesResponseDto>(DeserializeOptions);
        Assert.NotNull(body);
        var item = Assert.Single(body!.Items);
        Assert.Equal(cartonIds[0], item.CartonId);
    }

    [Fact]
    public async Task Listar_ConPageCero_Devuelve400()
    {
        var (comprador, _) = await NuevoCompradorAutenticadoConIdAsync();

        var respuesta = await comprador.GetAsync("/api/compradores/mis-cartones?page=0");

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var error = await respuesta.Content.ReadFromJsonAsync<ErrorResponseDto>(DeserializeOptions);
        Assert.NotNull(error);
        Assert.Equal("DatosInvalidos", error!.Error);
    }

    [Fact]
    public async Task Listar_SinRolComprador_Devuelve403()
    {
        using var organizador = await NuevoOrganizadorAutenticadoAsync();

        var respuesta = await organizador.GetAsync("/api/compradores/mis-cartones");

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task Listar_SinAutenticar_Devuelve401()
    {
        using var client = NuevoClienteHttps();

        var respuesta = await client.GetAsync("/api/compradores/mis-cartones");

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task Listar_SuperandoElLimiteDeSolicitudes_Devuelve429()
    {
        var (comprador, _) = await NuevoCompradorAutenticadoConIdAsync();

        // Política "comprador-cuenta": 30 permits / 5 min, particionada por el claim NameIdentifier.
        for (var intento = 1; intento <= 30; intento++)
        {
            var respuesta = await comprador.GetAsync("/api/compradores/mis-cartones");
            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        }

        var request31 = await comprador.GetAsync("/api/compradores/mis-cartones");

        Assert.Equal(HttpStatusCode.TooManyRequests, request31.StatusCode);
    }

    [Fact]
    public async Task Listar_SinCompras_Devuelve200ConListaVaciaYTotalCero()
    {
        var (comprador, _) = await NuevoCompradorAutenticadoConIdAsync();

        var respuesta = await comprador.GetAsync("/api/compradores/mis-cartones");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var body = await respuesta.Content.ReadFromJsonAsync<MisCartonesResponseDto>(DeserializeOptions);
        Assert.NotNull(body);
        Assert.Empty(body!.Items);
        Assert.Equal(0, body.Total);
        Assert.Equal(0, body.TotalPaginas);
    }

    // Spec FEAT-009d, Block 3 — AC-14 sobre las cuatro superficies que existen al cerrar este
    // bloque: un mismo cartón muestra el mismo correlativo en el descubrimiento, en el carrito, en
    // la respuesta de confirmación de compra, en el mail de confirmación y en "mis cartones".
    [Fact]
    public async Task Listar_ElCorrelativoCoincideConElDeLasOtrasSuperficies()
    {
        var (organizadorId, _, cartonIds) = await SembrarOrganizadorConBingoYCartonesAsync(
            "Club Mis Cartones Consistencia", 1, costoPorCarton: 100m);
        var cartonId = cartonIds[0];

        var (comprador, _) = await NuevoCompradorAutenticadoConIdAsync();

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
        var respuestaConfirmar = await confirmar.Content.ReadFromJsonAsync<ConfirmarCompraResponseDto>(DeserializeOptions);
        Assert.NotNull(respuestaConfirmar);
        var compraCreada = Assert.Single(respuestaConfirmar!.Compras);
        var correlativoEnConfirmacion = Assert.Single(compraCreada.Cartones).NumeroCorrelativo;

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;
        await using var context = new AppDbContext(options);
        var confirmacionId = (await context.Compras.AsNoTracking().SingleAsync(c => c.Id == compraCreada.CompraId)).ConfirmacionId;
        var datosMail = await new EnvioMailRepository(context).ObtenerDatosParaEnviarAsync(confirmacionId);
        Assert.NotNull(datosMail);
        var correlativoEnMail = Assert.Single(Assert.Single(datosMail!.Compras).Cartones).NumeroCorrelativo;

        var misCartones = await comprador.GetAsync("/api/compradores/mis-cartones");
        Assert.Equal(HttpStatusCode.OK, misCartones.StatusCode);
        var bodyMisCartones = await misCartones.Content.ReadFromJsonAsync<MisCartonesResponseDto>(DeserializeOptions);
        Assert.NotNull(bodyMisCartones);
        var correlativoEnMisCartones = Assert.Single(bodyMisCartones!.Items, i => i.CartonId == cartonId).NumeroCorrelativo;

        Assert.Equal(1, correlativoEnDescubrimiento);
        Assert.Equal(correlativoEnDescubrimiento, correlativoEnCarrito);
        Assert.Equal(correlativoEnDescubrimiento, correlativoEnConfirmacion);
        Assert.Equal(correlativoEnDescubrimiento, correlativoEnMail);
        Assert.Equal(correlativoEnDescubrimiento, correlativoEnMisCartones);
    }

    // Spec FEAT-009d, Block 4 — GET /api/compradores/mis-cartones/{cartonId:guid}/pdf.

    [Fact]
    public async Task DescargarPdf_ConCartonPropio_Devuelve200ConContentTypePdf()
    {
        var (organizadorId, _, cartonIds) = await SembrarOrganizadorConBingoYCartonesAsync(
            "Club PDF Propio", 1, costoPorCarton: 100m);
        var (comprador, compradorId) = await NuevoCompradorAutenticadoConIdAsync();
        await SembrarCompraConCartonAsync(
            organizadorId, compradorId, cartonIds[0], 100m, EstadoCompra.Confirmado);

        var respuesta = await comprador.GetAsync($"/api/compradores/mis-cartones/{cartonIds[0]}/pdf");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("application/pdf", respuesta.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", respuesta.Content.Headers.ContentDisposition?.DispositionType);
        var pdf = await respuesta.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(pdf);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public async Task DescargarPdf_ConCartonAjeno_Devuelve404()
    {
        var (organizadorId, _, cartonIds) = await SembrarOrganizadorConBingoYCartonesAsync(
            "Club PDF Ajeno", 1, costoPorCarton: 100m);
        var (compradorA, _) = await NuevoCompradorAutenticadoConIdAsync();
        var otroCompradorId = Guid.NewGuid();
        await SembrarCompraConCartonAsync(
            organizadorId, otroCompradorId, cartonIds[0], 100m, EstadoCompra.Confirmado);

        var respuesta = await compradorA.GetAsync($"/api/compradores/mis-cartones/{cartonIds[0]}/pdf");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        var error = await respuesta.Content.ReadFromJsonAsync<ErrorResponseDto>(DeserializeOptions);
        Assert.NotNull(error);
        Assert.Equal("CartonNoEncontrado", error!.Error);
    }

    // Anti-enumeración (R-01): el mismo comprador pide un cartón ajeno y uno que directamente no
    // existe — las dos respuestas 404 tienen que ser byte a byte idénticas, para que no se pueda
    // distinguir "existe pero no es mío" de "no existe en absoluto".
    [Fact]
    public async Task DescargarPdf_ConCartonInexistente_Devuelve404ConElMismoCuerpo()
    {
        var (organizadorId, _, cartonIds) = await SembrarOrganizadorConBingoYCartonesAsync(
            "Club PDF Inexistente", 1, costoPorCarton: 100m);
        var (compradorA, _) = await NuevoCompradorAutenticadoConIdAsync();
        var otroCompradorId = Guid.NewGuid();
        await SembrarCompraConCartonAsync(
            organizadorId, otroCompradorId, cartonIds[0], 100m, EstadoCompra.Confirmado);

        var respuestaAjeno = await compradorA.GetAsync($"/api/compradores/mis-cartones/{cartonIds[0]}/pdf");
        var respuestaInexistente = await compradorA.GetAsync($"/api/compradores/mis-cartones/{Guid.NewGuid()}/pdf");

        Assert.Equal(HttpStatusCode.NotFound, respuestaAjeno.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, respuestaInexistente.StatusCode);
        var cuerpoAjeno = await respuestaAjeno.Content.ReadAsStringAsync();
        var cuerpoInexistente = await respuestaInexistente.Content.ReadAsStringAsync();
        Assert.Equal(cuerpoAjeno, cuerpoInexistente);
    }

    // A-02: el PDF es comprobante de lo que ocurrió, no título de propiedad vigente — se permite
    // descargarlo aunque la compra que incluye el cartón haya sido cancelada.
    [Fact]
    public async Task DescargarPdf_ConCompraCancelada_PermiteLaDescarga()
    {
        var (organizadorId, _, cartonIds) = await SembrarOrganizadorConBingoYCartonesAsync(
            "Club PDF Cancelada", 1, costoPorCarton: 100m);
        var (comprador, compradorId) = await NuevoCompradorAutenticadoConIdAsync();
        await SembrarCompraConCartonAsync(
            organizadorId, compradorId, cartonIds[0], 100m, EstadoCompra.Cancelado);

        var respuesta = await comprador.GetAsync($"/api/compradores/mis-cartones/{cartonIds[0]}/pdf");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("application/pdf", respuesta.Content.Headers.ContentType?.MediaType);
    }

    // AC-13: un número correlativo (p. ej. "1") no es un GUID — el constraint de ruta ":guid" lo
    // resuelve como 404 de routing antes de que la request llegue al controller.
    [Fact]
    public async Task DescargarPdf_ConNumeroCorrelativoEnLugarDelGuid_NoDevuelveCarton()
    {
        var (comprador, _) = await NuevoCompradorAutenticadoConIdAsync();

        var respuesta = await comprador.GetAsync("/api/compradores/mis-cartones/1/pdf");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }

    [Fact]
    public async Task DescargarPdf_SinRolComprador_Devuelve403()
    {
        using var organizador = await NuevoOrganizadorAutenticadoAsync();

        var respuesta = await organizador.GetAsync($"/api/compradores/mis-cartones/{Guid.NewGuid()}/pdf");

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task DescargarPdf_SinAutenticar_Devuelve401()
    {
        using var client = NuevoClienteHttps();

        var respuesta = await client.GetAsync($"/api/compradores/mis-cartones/{Guid.NewGuid()}/pdf");

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task DescargarPdf_SuperandoElLimiteDeSolicitudes_Devuelve429()
    {
        var (organizadorId, _, cartonIds) = await SembrarOrganizadorConBingoYCartonesAsync(
            "Club PDF Rate Limit", 1, costoPorCarton: 100m);
        var (comprador, compradorId) = await NuevoCompradorAutenticadoConIdAsync();
        await SembrarCompraConCartonAsync(
            organizadorId, compradorId, cartonIds[0], 100m, EstadoCompra.Confirmado);

        // Política "comprador-cuenta": 30 permits / 5 min, particionada por el claim NameIdentifier —
        // la misma que Listar_SuperandoElLimiteDeSolicitudes_Devuelve429, reutilizada tal cual
        // (NFR-02): el endpoint que más trabajo de CPU genera por request es el que más necesita el
        // límite.
        for (var intento = 1; intento <= 30; intento++)
        {
            var respuesta = await comprador.GetAsync($"/api/compradores/mis-cartones/{cartonIds[0]}/pdf");
            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        }

        var request31 = await comprador.GetAsync($"/api/compradores/mis-cartones/{cartonIds[0]}/pdf");

        Assert.Equal(HttpStatusCode.TooManyRequests, request31.StatusCode);
    }

    private sealed record ErrorResponseDto(string Error, string Message);

    private sealed record RegistroCompradorDto(Guid Id, string Apellido, string Nombre, string Mail);

    private sealed record CartonAdquiridoDto(
        Guid CartonId,
        int NumeroCorrelativo,
        List<int> Numeros,
        string NombreBingo,
        string NombreOrganizacion,
        string EstadoCompra,
        Guid CompraId);

    private sealed record MisCartonesResponseDto(
        List<CartonAdquiridoDto> Items, int Total, int TotalPaginas, int Page, int PageSize);

    private sealed record CartonDescubiertoDto(
        Guid Id, int NumeroCorrelativo, string NombreOrganizacion, string NombreEvento, DateTime FechaSorteoUtc,
        decimal CostoPorCarton, List<int> Numeros);

    private sealed record ItemCarritoResponseDto(
        Guid CartonId, int NumeroCorrelativo, string NombreOrganizacion, string NombreEvento, decimal PrecioUnitario);

    private sealed record CarritoResponseDto(List<ItemCarritoResponseDto> Items, int CantidadTotal, decimal MontoTotal);

    private sealed record CartonCompradoDto(Guid CartonId, int NumeroCorrelativo);

    private sealed record CompraCreadaDto(
        Guid CompraId,
        Guid OrganizadorId,
        string NombreOrganizacion,
        int CantidadCartones,
        decimal MontoTotal,
        List<CartonCompradoDto> Cartones);

    private sealed record ConfirmarCompraResponseDto(List<CompraCreadaDto> Compras);
}
