using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using BingoCart.Application.Compradores.Dtos;
using BingoCart.Domain.Bingos;
using BingoCart.Domain.Compras;
using BingoCart.Infrastructure.Data;
using BingoCart.Infrastructure.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace BingoCart.Api.Tests.Controllers;

/// <summary>
/// Tests de integración de <c>CompradoresController</c> (spec FEAT-009a, Block 3; spec FEAT-009d,
/// Block 6) contra el stack completo levantado en memoria (<see cref="WebApplicationFactory{T}"/>)
/// y el SQL Server real dockerizado (puerto 14330) — mismo patrón que
/// <c>OrganizadoresControllerTests</c>: cada test crea su propio mail único (Rule #0 de
/// testing.instructions.md). El CUIT usa un generador propio con semilla aleatoria (en vez del
/// contador secuencial de <c>OrganizadoresControllerTests</c>/<c>BingosControllerTests</c>) porque
/// <c>AspNetUsers.Cuit</c> comparte el mismo índice único entre organizador y comprador (decisión de
/// PLAN, spec FEAT-009a): con dos clases de test corriendo en paralelo (xUnit, default) y un
/// contador que arranca en 0 en cada una, la primera llamada de cada clase generaría el MISMO CUIT.
///
/// El cleanup de <see cref="DisposeAsync"/> filtra por <c>Id</c> (nunca cambia), NO por Email
/// (spec FEAT-009d, Block 6, G-13): antes de este bloque filtraba por Email, y los tests de
/// <c>ActualizarCuenta</c> agregados acá CAMBIAN el mail del comprador, así que un filtro por Email
/// dejaría el usuario huérfano en la base compartida — con el índice único de Cuit, eso contamina
/// corridas futuras de OTROS tickets, no solo de este archivo.
/// </summary>
public sealed class CompradoresControllerTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Server=localhost,14330;Database=BingoCart;User Id=sa;" +
        "Password=BingoCart_Dev2026!;TrustServerCertificate=True;Encrypt=True;";

    private const string PasswordValida = "Passw0rd!";

    private const string TelefonoValido = "+54 11 4444-5555";

    private static readonly int[] MultiplicadoresCuit = { 5, 4, 3, 2, 7, 6, 5, 4, 3, 2 };

    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _userIdsCreados = new();
    private readonly List<Guid> _organizadorIdsCreados = new();
    private readonly List<Guid> _compraIdsCreadas = new();

    public CompradoresControllerTests()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        _client = _factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_compraIdsCreadas.Count > 0)
        {
            var optionsCompras = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(ConnectionString)
                .Options;
            await using var contextCompras = new AppDbContext(optionsCompras);

            var compraCartones = await contextCompras.CompraCartones
                .Where(cc => _compraIdsCreadas.Contains(cc.CompraId))
                .ToListAsync();
            contextCompras.CompraCartones.RemoveRange(compraCartones);
            var compras = await contextCompras.Compras
                .Where(c => _compraIdsCreadas.Contains(c.Id))
                .ToListAsync();
            contextCompras.Compras.RemoveRange(compras);
            await contextCompras.SaveChangesAsync();
        }

        if (_organizadorIdsCreados.Count > 0)
        {
            var optionsBingos = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(ConnectionString)
                .Options;
            await using var contextBingos = new AppDbContext(optionsBingos);

            var bingos = await contextBingos.Bingos
                .Where(b => _organizadorIdsCreados.Contains(b.OrganizadorId))
                .ToListAsync();
            var bingoIds = bingos.Select(b => b.Id).ToList();
            var cartones = await contextBingos.Cartones.Where(c => bingoIds.Contains(c.BingoId)).ToListAsync();
            contextBingos.Cartones.RemoveRange(cartones);
            contextBingos.Bingos.RemoveRange(bingos);
            await contextBingos.SaveChangesAsync();
        }

        await LimpiarUsuariosCreadosAsync();

        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // G-13 (spec FEAT-009d, Block 6): filtra por Id, nunca por Email. Extraído a su propio método
    // (en vez de vivir inline en DisposeAsync) para que
    // DisposeAsync_TrasCambiarElMail_LimpiaPorIdSinDejarUsuariosHuerfanos, abajo, pueda dispararlo a
    // mano y verificar su resultado ANTES del teardown real de xUnit.
    private async Task LimpiarUsuariosCreadosAsync()
    {
        if (_userIdsCreados.Count == 0)
        {
            return;
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        await using var context = new AppDbContext(options);
        var usuarios = await context.Users
            .Where(u => _userIdsCreados.Contains(u.Id))
            .ToListAsync();

        context.Users.RemoveRange(usuarios);
        await context.SaveChangesAsync();
    }

    // Semilla aleatoria (no un contador determinístico compartido entre clases de test, ver
    // comentario de clase) — reintenta hasta encontrar un dígito verificador válido, mismo algoritmo
    // CUIT/CUIL estándar que CuitValidator (Domain).
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

    private static string NuevoMail() => $"test-comprador-{Guid.NewGuid()}@example.com";

    private static object CrearBody(
        string? apellido = "Perez",
        string? nombre = "Juan",
        string? cuit = null,
        string? mail = null,
        string? password = PasswordValida)
    {
        return new
        {
            apellido,
            nombre,
            cuit = cuit ?? NuevoCuitValido(),
            mail = mail ?? NuevoMail(),
            password,
        };
    }

    private HttpClient NuevoClienteHttps() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

    /// <summary>
    /// Registra y loguea un comprador real vía los endpoints HTTP (mismo patrón que
    /// <c>MisCartonesControllerTests.NuevoCompradorAutenticadoConIdAsync</c>) — necesario para que
    /// los tests de <c>ActualizarCuenta</c> (Block 6) puedan enviar la cookie <c>bingocart_auth</c>
    /// en la request PUT subsiguiente (Secure, por eso <see cref="NuevoClienteHttps"/>).
    /// </summary>
    private async Task<(HttpClient Client, Guid Id, string Mail, string Cuit)> RegistrarYLoguearCompradorAsync()
    {
        var mail = NuevoMail();
        var cuit = NuevoCuitValido();
        var client = NuevoClienteHttps();

        var registro = await client.PostAsJsonAsync("/api/compradores/registro", CrearBody(mail: mail, cuit: cuit));
        registro.EnsureSuccessStatusCode();
        var contenido = await registro.Content.ReadFromJsonAsync<RegistrarCompradorResponse>(DeserializeOptions);
        Assert.NotNull(contenido);
        _userIdsCreados.Add(contenido!.Id);

        var login = await client.PostAsJsonAsync("/api/compradores/login", new { mail, password = PasswordValida });
        login.EnsureSuccessStatusCode();

        return (client, contenido.Id, mail, cuit);
    }

    /// <summary>
    /// Registra y loguea un organizador real (mismo patrón que
    /// <c>MisCartonesControllerTests.NuevoOrganizadorAutenticadoAsync</c>) — necesario para el test
    /// que verifica que <c>[Authorize(Roles = "Comprador")]</c> rechaza a un organizador.
    /// </summary>
    private async Task<HttpClient> NuevoOrganizadorAutenticadoAsync()
    {
        var mail = $"test-comprador-cuenta-organizador-{Guid.NewGuid()}@example.com";
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
        var contenido = await registro.Content.ReadFromJsonAsync<RegistrarOrganizadorResponseDto>(DeserializeOptions);
        Assert.NotNull(contenido);
        _userIdsCreados.Add(contenido!.Id);

        var login = await client.PostAsJsonAsync("/api/organizadores/login", new { mail, password = PasswordValida });
        login.EnsureSuccessStatusCode();

        return client;
    }

    /// <summary>
    /// Siembra un organizador (fila propia en AspNetUsers), un bingo cuyo sorteo cae dentro de
    /// <paramref name="enCuanto"/> y un único cartón — necesario para
    /// <c>ActualizarCuenta_ConSorteoInminente_Devuelve409PlazoModificacionVencido</c> (regla de los
    /// 60 minutos, FR-07).
    /// </summary>
    private async Task<(Guid OrganizadorId, Guid CartonId)> SembrarBingoConSorteoEnAsync(TimeSpan enCuanto)
    {
        var organizadorId = Guid.NewGuid();
        var mailOrganizador = $"test-comprador-cuenta-org-{organizadorId}@example.com";
        var organizador = new ApplicationUser
        {
            Id = organizadorId,
            UserName = mailOrganizador,
            Email = mailOrganizador,
            NombreOrganizacion = "Club Datos de Cuenta",
            Cuit = NuevoCuitValido(),
            Telefono = TelefonoValido,
        };

        var ahoraUtc = DateTime.UtcNow;
        var bingo = Bingo.Crear(
            "Bingo con sorteo inminente", ahoraUtc.Add(enCuanto), 1, 100m, organizadorId, ahoraUtc.AddDays(-1));
        var carton = Carton.Crear(bingo.Id, Enumerable.Range(1, 10).ToArray(), numeroCorrelativo: 1);

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;
        await using var context = new AppDbContext(options);
        context.Users.Add(organizador);
        context.Bingos.Add(bingo);
        context.Cartones.Add(carton);
        await context.SaveChangesAsync();

        _organizadorIdsCreados.Add(organizadorId);
        _userIdsCreados.Add(organizadorId);

        return (organizadorId, carton.Id);
    }

    /// <summary>
    /// Siembra una <see cref="Compra"/> vigente (no <c>Cancelado</c>) que le da al comprador un
    /// cartón vigente sobre <paramref name="cartonId"/> — la condición que dispara
    /// <c>TieneSorteoInminenteAsync</c> (D-02).
    /// </summary>
    private async Task<Compra> SembrarCompraAsync(Guid organizadorId, Guid compradorId, Guid cartonId, decimal monto)
    {
        var compra = Compra.Crear(
            organizadorId,
            compradorId,
            Guid.NewGuid(),
            new[] { new ItemCompra(cartonId, monto) },
            MedioPago.Efectivo,
            DateTime.UtcNow);

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;
        await using var context = new AppDbContext(options);
        context.Compras.Add(compra);
        context.CompraCartones.Add(new CompraCarton { CompraId = compra.Id, CartonId = cartonId, PrecioUnitario = monto });
        await context.SaveChangesAsync();

        _compraIdsCreadas.Add(compra.Id);

        return compra;
    }

    [Fact]
    public async Task Registro_ConDatosValidos_Devuelve201YElUsuarioQuedaConRolComprador()
    {
        var mail = NuevoMail();
        var body = CrearBody(mail: mail);

        var response = await _client.PostAsJsonAsync("/api/compradores/registro", body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<RegistrarCompradorResponse>(DeserializeOptions);
        Assert.NotNull(content);
        Assert.NotEqual(Guid.Empty, content!.Id);
        _userIdsCreados.Add(content.Id);
        Assert.Equal("Perez", content.Apellido);
        Assert.Equal("Juan", content.Nombre);
        Assert.Equal(mail, content.Mail);

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;
        await using var context = new AppDbContext(options);
        var usuario = await context.Users.SingleAsync(u => u.Email == mail);
        var rolesDelUsuario =
            from userRole in context.UserRoles
            join role in context.Roles on userRole.RoleId equals role.Id
            where userRole.UserId == usuario.Id
            select role.Name;

        Assert.Contains("Comprador", await rolesDelUsuario.ToListAsync());
    }

    [Fact]
    public async Task Registro_ConCuitInvalido_Devuelve400CuitInvalido()
    {
        var mail = NuevoMail();
        var body = CrearBody(mail: mail, cuit: "12345");

        var response = await _client.PostAsJsonAsync("/api/compradores/registro", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponseDto>(DeserializeOptions);
        Assert.NotNull(error);
        Assert.Equal("CuitInvalido", error!.Error);
    }

    [Fact]
    public async Task Registro_ConMailDuplicado_Devuelve409MailYaRegistrado()
    {
        var mail = NuevoMail();

        var primerRegistro = await _client.PostAsJsonAsync("/api/compradores/registro", CrearBody(mail: mail));
        Assert.Equal(HttpStatusCode.Created, primerRegistro.StatusCode);
        var primerContenido = await primerRegistro.Content.ReadFromJsonAsync<RegistrarCompradorResponse>(DeserializeOptions);
        Assert.NotNull(primerContenido);
        _userIdsCreados.Add(primerContenido!.Id);

        var segundoRegistro = await _client.PostAsJsonAsync(
            "/api/compradores/registro", CrearBody(mail: mail, cuit: NuevoCuitValido()));

        Assert.Equal(HttpStatusCode.Conflict, segundoRegistro.StatusCode);
        var error = await segundoRegistro.Content.ReadFromJsonAsync<ErrorResponseDto>(DeserializeOptions);
        Assert.NotNull(error);
        Assert.Equal("MailYaRegistrado", error!.Error);
    }

    [Fact]
    public async Task Registro_ConPasswordInvalida_Devuelve400PasswordInvalida()
    {
        var mail = NuevoMail();
        var body = CrearBody(mail: mail, password: "abc");

        var response = await _client.PostAsJsonAsync("/api/compradores/registro", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponseDto>(DeserializeOptions);
        Assert.NotNull(error);
        Assert.Equal("PasswordInvalida", error!.Error);
    }

    [Fact]
    public async Task Login_ConCredencialesValidas_Devuelve200YFijaLaCookieDeAutenticacionConRolComprador()
    {
        var mail = NuevoMail();
        var registro = await _client.PostAsJsonAsync("/api/compradores/registro", CrearBody(mail: mail));
        Assert.Equal(HttpStatusCode.Created, registro.StatusCode);
        var registroContenido = await registro.Content.ReadFromJsonAsync<RegistrarCompradorResponse>(DeserializeOptions);
        Assert.NotNull(registroContenido);
        _userIdsCreados.Add(registroContenido!.Id);

        var response = await _client.PostAsJsonAsync(
            "/api/compradores/login",
            new { mail, password = PasswordValida });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies));
        var cookie = string.Join("", cookies!);
        Assert.StartsWith("bingocart_auth=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);

        // El JWT viaja en el valor de la cookie (mismo mecanismo que bingocart_auth de organizador):
        // se decodifica sin validar firma para leer únicamente el claim "role" (JwtSecurityToken no
        // requiere la clave de firma para leer los claims, solo para validarla).
        var valorCookie = cookie[(cookie.IndexOf('=') + 1)..];
        var finDelValor = valorCookie.IndexOf(';');
        var token = Uri.UnescapeDataString(finDelValor >= 0 ? valorCookie[..finDelValor] : valorCookie);

        // JwtSecurityTokenHandler.ReadJwtToken (lectura sin validar firma) ya remapea el claim corto
        // "role" al URI largo de ClaimTypes.Role al construir JwtSecurityToken.Claims (mismo
        // DefaultInboundClaimTypeMap que aplica ValidateToken) — se verifica contra ese URI, no
        // contra el nombre corto "role" que sí viaja en el JWT crudo.
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        Assert.Contains(jwt.Claims, c => c.Type == ClaimTypes.Role && c.Value == "Comprador");
    }

    // Spec FEAT-009d, Block 6 — PUT /api/compradores/mi-cuenta.

    [Fact]
    public async Task ActualizarCuenta_ConDatosValidos_Devuelve200ConElEstadoActualizado()
    {
        var (comprador, _, _, _) = await RegistrarYLoguearCompradorAsync();
        var cuitNuevo = NuevoCuitValido();
        var mailNuevo = $"test-comprador-actualizado-{Guid.NewGuid()}@example.com";
        var body = new
        {
            apellido = "Fernandez",
            nombre = "Lucia",
            cuit = cuitNuevo,
            mail = mailNuevo,
            contrasenaActual = PasswordValida,
        };

        var respuesta = await comprador.PutAsJsonAsync("/api/compradores/mi-cuenta", body);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var contenido = await respuesta.Content.ReadFromJsonAsync<CuentaCompradorResponse>(DeserializeOptions);
        Assert.NotNull(contenido);
        Assert.Equal("Fernandez", contenido!.Apellido);
        Assert.Equal("Lucia", contenido.Nombre);
        Assert.Equal(cuitNuevo, contenido.Cuit);
        Assert.Equal(mailNuevo, contenido.Mail);
    }

    [Fact]
    public async Task ActualizarCuenta_ConDatosValidos_ReemiteLaCookieDeSesion()
    {
        var (comprador, _, _, _) = await RegistrarYLoguearCompradorAsync();
        var mailNuevo = $"test-comprador-cookie-{Guid.NewGuid()}@example.com";
        var body = new
        {
            apellido = "Fernandez",
            nombre = "Lucia",
            cuit = NuevoCuitValido(),
            mail = mailNuevo,
            contrasenaActual = PasswordValida,
        };

        var respuesta = await comprador.PutAsJsonAsync("/api/compradores/mi-cuenta", body);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.True(respuesta.Headers.TryGetValues("Set-Cookie", out var cookies));
        var cookie = cookies!.Single(c => c.StartsWith("bingocart_auth=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);

        var valorCookie = cookie[(cookie.IndexOf('=') + 1)..];
        var finDelValor = valorCookie.IndexOf(';');
        var token = Uri.UnescapeDataString(finDelValor >= 0 ? valorCookie[..finDelValor] : valorCookie);
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        Assert.Contains(jwt.Claims, c => c.Type == ClaimTypes.Email && c.Value == mailNuevo);
    }

    [Fact]
    public async Task ActualizarCuenta_ConSorteoInminente_Devuelve409PlazoModificacionVencido()
    {
        var (comprador, compradorId, _, _) = await RegistrarYLoguearCompradorAsync();
        var (organizadorId, cartonId) = await SembrarBingoConSorteoEnAsync(TimeSpan.FromMinutes(30));
        await SembrarCompraAsync(organizadorId, compradorId, cartonId, 100m);

        var body = new
        {
            apellido = "Fernandez",
            nombre = "Lucia",
            cuit = NuevoCuitValido(),
            mail = $"test-comprador-plazo-{Guid.NewGuid()}@example.com",
            contrasenaActual = PasswordValida,
        };

        var respuesta = await comprador.PutAsJsonAsync("/api/compradores/mi-cuenta", body);

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        var error = await respuesta.Content.ReadFromJsonAsync<ErrorResponseDto>(DeserializeOptions);
        Assert.NotNull(error);
        Assert.Equal("PlazoModificacionVencido", error!.Error);
    }

    [Fact]
    public async Task ActualizarCuenta_ConMailDeOtraCuenta_Devuelve409MailEnUso()
    {
        var (compradorA, _, _, _) = await RegistrarYLoguearCompradorAsync();
        var (_, _, mailB, _) = await RegistrarYLoguearCompradorAsync();

        var body = new
        {
            apellido = "Fernandez",
            nombre = "Lucia",
            cuit = NuevoCuitValido(),
            mail = mailB,
            contrasenaActual = PasswordValida,
        };

        var respuesta = await compradorA.PutAsJsonAsync("/api/compradores/mi-cuenta", body);

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        var error = await respuesta.Content.ReadFromJsonAsync<ErrorResponseDto>(DeserializeOptions);
        Assert.NotNull(error);
        Assert.Equal("MailEnUso", error!.Error);
    }

    [Fact]
    public async Task ActualizarCuenta_ConCuitDeOtraCuenta_Devuelve409CuitEnUso()
    {
        var (compradorA, _, _, _) = await RegistrarYLoguearCompradorAsync();
        var (_, _, _, cuitB) = await RegistrarYLoguearCompradorAsync();

        var body = new
        {
            apellido = "Fernandez",
            nombre = "Lucia",
            cuit = cuitB,
            mail = $"test-comprador-cuitenuso-{Guid.NewGuid()}@example.com",
            contrasenaActual = PasswordValida,
        };

        var respuesta = await compradorA.PutAsJsonAsync("/api/compradores/mi-cuenta", body);

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        var error = await respuesta.Content.ReadFromJsonAsync<ErrorResponseDto>(DeserializeOptions);
        Assert.NotNull(error);
        Assert.Equal("CuitEnUso", error!.Error);
    }

    [Fact]
    public async Task ActualizarCuenta_ConCuitDeLongitudInvalida_Devuelve400CuitInvalido()
    {
        var (comprador, _, _, _) = await RegistrarYLoguearCompradorAsync();
        var body = new
        {
            apellido = "Fernandez",
            nombre = "Lucia",
            cuit = "12345",
            mail = $"test-comprador-cuitlongitud-{Guid.NewGuid()}@example.com",
            contrasenaActual = PasswordValida,
        };

        var respuesta = await comprador.PutAsJsonAsync("/api/compradores/mi-cuenta", body);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var error = await respuesta.Content.ReadFromJsonAsync<ErrorResponseDto>(DeserializeOptions);
        Assert.NotNull(error);
        Assert.Equal("CuitInvalido", error!.Error);
    }

    [Fact]
    public async Task ActualizarCuenta_ConDigitoVerificadorInvalido_Devuelve400CuitInvalido()
    {
        var (comprador, _, _, _) = await RegistrarYLoguearCompradorAsync();
        var body = new
        {
            apellido = "Fernandez",
            nombre = "Lucia",
            // Mismo CUIT de 11 dígitos numéricos que CuitValido ("30500010912") con el último dígito
            // alterado — conocido inválido por dígito verificador (CompradorServiceTests, mismo
            // valor).
            cuit = "30500010910",
            mail = $"test-comprador-digito-{Guid.NewGuid()}@example.com",
            contrasenaActual = PasswordValida,
        };

        var respuesta = await comprador.PutAsJsonAsync("/api/compradores/mi-cuenta", body);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var error = await respuesta.Content.ReadFromJsonAsync<ErrorResponseDto>(DeserializeOptions);
        Assert.NotNull(error);
        Assert.Equal("CuitInvalido", error!.Error);
    }

    [Fact]
    public async Task ActualizarCuenta_ConApellidoVacio_Devuelve400DatosInvalidos()
    {
        var (comprador, _, _, _) = await RegistrarYLoguearCompradorAsync();
        var body = new
        {
            apellido = "",
            nombre = "Lucia",
            cuit = NuevoCuitValido(),
            mail = $"test-comprador-apellidovacio-{Guid.NewGuid()}@example.com",
            contrasenaActual = PasswordValida,
        };

        var respuesta = await comprador.PutAsJsonAsync("/api/compradores/mi-cuenta", body);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var error = await respuesta.Content.ReadFromJsonAsync<ErrorResponseDto>(DeserializeOptions);
        Assert.NotNull(error);
        Assert.Equal("DatosInvalidos", error!.Error);
    }

    [Fact]
    public async Task ActualizarCuenta_SinRolComprador_Devuelve403()
    {
        using var organizador = await NuevoOrganizadorAutenticadoAsync();
        var body = new
        {
            apellido = "Fernandez",
            nombre = "Lucia",
            cuit = NuevoCuitValido(),
            mail = $"test-comprador-sinrol-{Guid.NewGuid()}@example.com",
            contrasenaActual = PasswordValida,
        };

        var respuesta = await organizador.PutAsJsonAsync("/api/compradores/mi-cuenta", body);

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task ActualizarCuenta_SinAutenticar_Devuelve401()
    {
        using var client = NuevoClienteHttps();
        var body = new
        {
            apellido = "Fernandez",
            nombre = "Lucia",
            cuit = NuevoCuitValido(),
            mail = $"test-comprador-sinautenticar-{Guid.NewGuid()}@example.com",
            contrasenaActual = PasswordValida,
        };

        var respuesta = await client.PutAsJsonAsync("/api/compradores/mi-cuenta", body);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task ActualizarCuenta_ConContrasenaIncorrecta_Devuelve403SinModificarNada()
    {
        var (comprador, compradorId, mailOriginal, _) = await RegistrarYLoguearCompradorAsync();
        var body = new
        {
            apellido = "Fernandez",
            nombre = "Lucia",
            cuit = NuevoCuitValido(),
            mail = $"test-comprador-claveincorrecta-{Guid.NewGuid()}@example.com",
            contrasenaActual = "ClaveIncorrecta1!",
        };

        var respuesta = await comprador.PutAsJsonAsync("/api/compradores/mi-cuenta", body);

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        var error = await respuesta.Content.ReadFromJsonAsync<ErrorResponseDto>(DeserializeOptions);
        Assert.NotNull(error);
        Assert.Equal("ContrasenaIncorrecta", error!.Error);

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;
        await using var context = new AppDbContext(options);
        var usuario = await context.Users.SingleAsync(u => u.Id == compradorId);
        Assert.Equal(mailOriginal, usuario.Email);
        Assert.Equal("Perez", usuario.Apellido);
        Assert.Equal("Juan", usuario.Nombre);
    }

    [Fact]
    public async Task ActualizarCuenta_SinContrasena_Devuelve400DatosInvalidos()
    {
        var (comprador, _, _, _) = await RegistrarYLoguearCompradorAsync();
        var body = new
        {
            apellido = "Fernandez",
            nombre = "Lucia",
            cuit = NuevoCuitValido(),
            mail = $"test-comprador-sinclave-{Guid.NewGuid()}@example.com",
        };

        var respuesta = await comprador.PutAsJsonAsync("/api/compradores/mi-cuenta", body);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var error = await respuesta.Content.ReadFromJsonAsync<ErrorResponseDto>(DeserializeOptions);
        Assert.NotNull(error);
        Assert.Equal("DatosInvalidos", error!.Error);
    }

    [Fact]
    public async Task ActualizarCuenta_LaRespuestaNoIncluyeLaContrasena()
    {
        var (comprador, _, _, _) = await RegistrarYLoguearCompradorAsync();
        var body = new
        {
            apellido = "Fernandez",
            nombre = "Lucia",
            cuit = NuevoCuitValido(),
            mail = $"test-comprador-respuestasinclave-{Guid.NewGuid()}@example.com",
            contrasenaActual = PasswordValida,
        };

        var respuesta = await comprador.PutAsJsonAsync("/api/compradores/mi-cuenta", body);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var crudo = await respuesta.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(crudo);
        var propiedades = json.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain(propiedades, p => p.Contains("contrasena", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propiedades, p => p.Contains("password", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "apellido", "nombre", "cuit", "mail" },
            propiedades);
    }

    [Fact]
    public async Task ActualizarCuenta_SuperandoElLimiteDeSolicitudes_Devuelve429()
    {
        var (comprador, _, mailOriginal, cuitOriginal) = await RegistrarYLoguearCompradorAsync();
        var body = new
        {
            apellido = "Perez",
            nombre = "Juan",
            cuit = cuitOriginal,
            mail = mailOriginal,
            contrasenaActual = PasswordValida,
        };

        // Política "comprador-cuenta": 30 permits / 5 min, particionada por el claim NameIdentifier —
        // se reenvían los mismos valores ya vigentes, así que ninguna de las 30 llamadas colisiona
        // con "mail/cuit de otra cuenta" ni dispara la regla de los 60 minutos (sin bingo sembrado).
        for (var intento = 1; intento <= 30; intento++)
        {
            var respuesta = await comprador.PutAsJsonAsync("/api/compradores/mi-cuenta", body);
            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        }

        var request31 = await comprador.PutAsJsonAsync("/api/compradores/mi-cuenta", body);

        Assert.Equal(HttpStatusCode.TooManyRequests, request31.StatusCode);
    }

    // G-13: demuestra el mecanismo de limpieza en sí, no un comportamiento HTTP nuevo. Sin la
    // corrección, LimpiarUsuariosCreadosAsync filtraba por Email — un usuario cuyo mail cambió
    // (como en los tests de ActualizarCuenta de arriba) no era encontrado por ese filtro y quedaba
    // huérfano en la base compartida. Acá se simula el cambio de mail directo contra la base (sin
    // depender del endpoint PUT bajo test) para aislar la prueba del mecanismo de limpieza en sí.
    [Fact]
    public async Task DisposeAsync_TrasCambiarElMail_LimpiaPorIdSinDejarUsuariosHuerfanos()
    {
        var mailOriginal = NuevoMail();
        var registro = await _client.PostAsJsonAsync("/api/compradores/registro", CrearBody(mail: mailOriginal));
        Assert.Equal(HttpStatusCode.Created, registro.StatusCode);
        var contenido = await registro.Content.ReadFromJsonAsync<RegistrarCompradorResponse>(DeserializeOptions);
        Assert.NotNull(contenido);
        var userId = contenido!.Id;
        _userIdsCreados.Add(userId);

        var mailNuevo = $"test-comprador-cambiado-{Guid.NewGuid()}@example.com";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;
        await using (var context = new AppDbContext(options))
        {
            var usuario = await context.Users.SingleAsync(u => u.Id == userId);
            usuario.Email = mailNuevo;
            usuario.NormalizedEmail = mailNuevo.ToUpperInvariant();
            usuario.UserName = mailNuevo;
            usuario.NormalizedUserName = mailNuevo.ToUpperInvariant();
            await context.SaveChangesAsync();
        }

        // Dispara el mismo mecanismo que DisposeAsync invoca al final de cada test — a mano, para
        // poder verificar el resultado ANTES del teardown real de xUnit.
        await LimpiarUsuariosCreadosAsync();

        await using (var contextVerificacion = new AppDbContext(options))
        {
            var quedoHuerfano = await contextVerificacion.Users.AnyAsync(u => u.Id == userId);
            Assert.False(quedoHuerfano, "El usuario cuyo mail cambió durante el test quedó huérfano tras la limpieza.");
        }

        // Ya se limpió a mano — evita que DisposeAsync intente de nuevo (sería un no-op, pero
        // explícito).
        _userIdsCreados.Remove(userId);
    }

    private sealed record ErrorResponseDto(string Error, string Message);

    private sealed record RegistrarOrganizadorResponseDto(Guid Id, string NombreOrganizacion, string Mail);
}
