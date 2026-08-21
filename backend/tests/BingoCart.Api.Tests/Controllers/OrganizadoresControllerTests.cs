using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BingoCart.Application.Auth;
using BingoCart.Application.Organizadores.Dtos;
using BingoCart.Domain.Bingos;
using BingoCart.Infrastructure.Auth;
using BingoCart.Infrastructure.Data;
using BingoCart.Infrastructure.Identity;
using BingoCart.Infrastructure.Tests.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BingoCart.Api.Tests.Controllers;

/// <summary>
/// Tests de integración del endpoint <c>POST /api/organizadores/registro</c> (Block 4 del spec
/// FEAT-001a) contra el stack completo levantado en memoria (<see cref="WebApplicationFactory{T}"/>)
/// y el SQL Server real dockerizado (<c>docker compose up -d db</c>, puerto 14330). Cada test crea
/// su propio mail único (Rule #0 de testing.instructions.md) y lo elimina en <see cref="DisposeAsync"/>.
/// </summary>
public sealed class OrganizadoresControllerTests : IAsyncLifetime
{
    // Misma connection string de desarrollo que appsettings.Development.json (Block 1) — el
    // contenedor `db` de docker-compose.yml ya debe estar corriendo antes de ejecutar estos tests.
    private const string ConnectionString =
        "Server=localhost,14330;Database=BingoCart;User Id=sa;" +
        "Password=BingoCart_Dev2026!;TrustServerCertificate=True;Encrypt=True;";

    private const string CuitValido = "30500010912";
    private const string TelefonoValido = "+54 11 4444-5555";
    private const string PasswordValida = "Passw0rd!";

    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly List<string> _mailsCreados = new();

    // Block 2 del spec FEAT-005: los tests del directorio siembran organizadores (algunos vía
    // registro real, otros directo contra AppDbContext) y sus bingos activos — se rastrean acá para
    // que DisposeAsync borre también los Bingos antes que los Users (mismo orden que
    // BingosControllerTests.DisposeAsync, evita violar la FK lógica).
    private readonly List<Guid> _organizadorIdsCreados = new();

    // Multiplicadores del algoritmo estándar CUIT/CUIL (mismo mecanismo que
    // BingosControllerTests.NuevoCuitValido): RegistrarYLoguearAsync pasa por
    // POST /api/organizadores/registro real, que valida el CUIT con Organizador.Crear — a
    // diferencia de la constante CuitValido (compartida, colisiona con más de un organizador),
    // este generador produce un CUIT válido distinto por cada organizador sembrado por este bloque.
    private static readonly int[] MultiplicadoresCuit = { 5, 4, 3, 2, 7, 6, 5, 4, 3, 2 };
    private static long _secuenciaCuit;

    private static string NuevoCuitValido()
    {
        while (true)
        {
            var secuencia = System.Threading.Interlocked.Increment(ref _secuenciaCuit);
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
                continue;
            }

            return cuerpo + digitoVerificador;
        }
    }

    public OrganizadoresControllerTests()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        _client = _factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_mailsCreados.Count > 0 || _organizadorIdsCreados.Count > 0)
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
                context.Bingos.RemoveRange(bingos);
                await context.SaveChangesAsync();
            }

            var usuarios = await context.Users
                .Where(u => u.Email != null && _mailsCreados.Contains(u.Email))
                .ToListAsync();

            context.Users.RemoveRange(usuarios);
            await context.SaveChangesAsync();
        }

        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private static object CrearBody(
        string? nombreOrganizacion = "Club Social y Deportivo",
        string? cuit = CuitValido,
        string? mail = null,
        string? telefono = TelefonoValido,
        string? password = PasswordValida)
    {
        return new
        {
            nombreOrganizacion,
            cuit,
            mail = mail ?? $"test-{Guid.NewGuid()}@example.com",
            telefono,
            password,
        };
    }

    private string NuevoMail()
    {
        var mail = $"test-{Guid.NewGuid()}@example.com";
        _mailsCreados.Add(mail);
        return mail;
    }

    [Fact]
    public async Task Registro_ConDatosValidos_Devuelve201()
    {
        var mail = NuevoMail();
        var body = CrearBody(mail: mail);

        var response = await _client.PostAsJsonAsync("/api/organizadores/registro", body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<RegistrarOrganizadorResponse>(DeserializeOptions);

        Assert.NotNull(content);
        Assert.NotEqual(Guid.Empty, content!.Id);
        Assert.Equal("Club Social y Deportivo", content.NombreOrganizacion);
        Assert.Equal(mail, content.Mail);
    }

    [Fact]
    public async Task Registro_ConCuitInvalido_Devuelve400CuitInvalido()
    {
        var mail = NuevoMail();
        var body = CrearBody(mail: mail, cuit: "12345");

        var response = await _client.PostAsJsonAsync("/api/organizadores/registro", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponseDto>(DeserializeOptions);

        Assert.NotNull(error);
        Assert.Equal("CuitInvalido", error!.Error);
    }

    [Fact]
    public async Task Registro_ConMailDuplicado_Devuelve409MailYaRegistrado()
    {
        var mail = NuevoMail();

        var primerRegistro = await _client.PostAsJsonAsync("/api/organizadores/registro", CrearBody(mail: mail));
        Assert.Equal(HttpStatusCode.Created, primerRegistro.StatusCode);

        var segundoRegistro = await _client.PostAsJsonAsync("/api/organizadores/registro", CrearBody(mail: mail));

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

        var response = await _client.PostAsJsonAsync("/api/organizadores/registro", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponseDto>(DeserializeOptions);

        Assert.NotNull(error);
        Assert.Equal("PasswordInvalida", error!.Error);
    }

    [Fact]
    public async Task Registro_ConTelefonoInvalido_Devuelve400TelefonoInvalido()
    {
        var mail = NuevoMail();
        var body = CrearBody(mail: mail, telefono: "123");

        var response = await _client.PostAsJsonAsync("/api/organizadores/registro", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponseDto>(DeserializeOptions);

        Assert.NotNull(error);
        Assert.Equal("TelefonoInvalido", error!.Error);
    }

    [Fact]
    public async Task Registro_ConDatosValidos_PasswordSeAlmacenaComoHash()
    {
        var mail = NuevoMail();
        var body = CrearBody(mail: mail, password: PasswordValida);

        var response = await _client.PostAsJsonAsync("/api/organizadores/registro", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        await using var context = new AppDbContext(options);
        var usuario = await context.Users.SingleAsync(u => u.Email == mail);

        Assert.NotNull(usuario.PasswordHash);
        Assert.NotEqual(PasswordValida, usuario.PasswordHash);
        Assert.DoesNotContain(PasswordValida, usuario.PasswordHash!);
    }

    [Fact]
    public async Task Registro_ConNombreOrganizacionVacioOMailMalformado_Devuelve400DatosInvalidos()
    {
        var body = CrearBody(nombreOrganizacion: "", mail: "esto-no-es-un-mail");

        var response = await _client.PostAsJsonAsync("/api/organizadores/registro", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponseDto>(DeserializeOptions);

        Assert.NotNull(error);
        Assert.Equal("DatosInvalidos", error!.Error);
    }

    [Fact]
    public async Task Login_ConPasswordCorrecta_Devuelve200ConCookieHttpOnlySinTokenEnBodyYRapido()
    {
        var mail = NuevoMail();
        var registro = await _client.PostAsJsonAsync("/api/organizadores/registro", CrearBody(mail: mail));
        Assert.Equal(HttpStatusCode.Created, registro.StatusCode);

        var stopwatch = Stopwatch.StartNew();
        var response = await _client.PostAsJsonAsync(
            "/api/organizadores/login",
            new { mail, password = PasswordValida });
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"El login tardó {stopwatch.Elapsed}.");

        // El header Set-Cookie contiene una coma dentro del atributo `expires` (formato RFC 1123,
        // ej. "expires=Mon, 17 Aug 2026..."), que HttpHeaders interpreta como separador de una
        // lista de valores y parte en dos fragmentos — se unen para poder inspeccionar la cookie
        // completa en una sola cadena, en vez de asumir un único valor.
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies));
        var cookie = string.Join("", cookies!);
        Assert.StartsWith("bingocart_auth=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);

        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.TryGetProperty("expiraEnUtc", out _));
        Assert.False(json.RootElement.TryGetProperty("token", out _));
    }

    [Fact]
    public async Task Login_ConPasswordIncorrecta_Devuelve401SinSetCookie()
    {
        var mail = NuevoMail();
        var registro = await _client.PostAsJsonAsync("/api/organizadores/registro", CrearBody(mail: mail));
        Assert.Equal(HttpStatusCode.Created, registro.StatusCode);

        var response = await _client.PostAsJsonAsync(
            "/api/organizadores/login",
            new { mail, password = "PasswordIncorrecta1!" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));

        var error = await response.Content.ReadFromJsonAsync<ErrorResponseDto>(DeserializeOptions);
        Assert.NotNull(error);
        Assert.Equal("CredencialesInvalidas", error!.Error);
    }

    [Fact]
    public async Task Login_ConMailMalformado_Devuelve400DatosInvalidos()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/organizadores/login",
            new { mail = "esto-no-es-un-mail", password = PasswordValida });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponseDto>(DeserializeOptions);
        Assert.NotNull(error);
        Assert.Equal("DatosInvalidos", error!.Error);
    }

    [Fact]
    public async Task Login_ConPasswordVacia_Devuelve400DatosInvalidos()
    {
        var mail = NuevoMail();

        var response = await _client.PostAsJsonAsync(
            "/api/organizadores/login",
            new { mail, password = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponseDto>(DeserializeOptions);
        Assert.NotNull(error);
        Assert.Equal("DatosInvalidos", error!.Error);
    }

    [Fact]
    public async Task Login_Con5IntentosFallidosPrevios_Bloquea6toIntentoAunqueLaPasswordSeaCorrecta()
    {
        var mail = NuevoMail();
        var registro = await _client.PostAsJsonAsync("/api/organizadores/registro", CrearBody(mail: mail));
        Assert.Equal(HttpStatusCode.Created, registro.StatusCode);

        for (var intento = 1; intento <= 5; intento++)
        {
            var intentoFallido = await _client.PostAsJsonAsync(
                "/api/organizadores/login",
                new { mail, password = "PasswordIncorrecta1!" });

            Assert.Equal(HttpStatusCode.Unauthorized, intentoFallido.StatusCode);
        }

        var sextoIntentoConPasswordCorrecta = await _client.PostAsJsonAsync(
            "/api/organizadores/login",
            new { mail, password = PasswordValida });

        Assert.Equal(HttpStatusCode.Unauthorized, sextoIntentoConPasswordCorrecta.StatusCode);
        Assert.False(sextoIntentoConPasswordCorrecta.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Perfil_SinCookieDeAutenticacion_Devuelve401()
    {
        var response = await _client.GetAsync("/api/organizadores/perfil");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Perfil_ConCookieDeLoginReal_Devuelve200ConElMailCorrecto()
    {
        var mail = NuevoMail();
        var registro = await _client.PostAsJsonAsync("/api/organizadores/registro", CrearBody(mail: mail));
        Assert.Equal(HttpStatusCode.Created, registro.StatusCode);

        // Cliente dedicado con BaseAddress https://: la cookie `bingocart_auth` se fija con
        // `Secure = true` (Block 2), y un CookieContainer nunca reenvía una cookie Secure sobre un
        // origen http:// — el BaseAddress por defecto de WebApplicationFactory.CreateClient() es
        // http://localhost, así que con _client la cookie quedaría guardada pero jamás se
        // reenviaría a /perfil. El resto del comportamiento (HandleCookies = true por defecto)
        // sigue siendo el mismo manejo automático de cookies de un navegador real.
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var login = await client.PostAsJsonAsync(
            "/api/organizadores/login",
            new { mail, password = PasswordValida });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var perfil = await client.GetAsync("/api/organizadores/perfil");

        Assert.Equal(HttpStatusCode.OK, perfil.StatusCode);

        var content = await perfil.Content.ReadFromJsonAsync<PerfilOrganizadorResponseDto>(DeserializeOptions);
        Assert.NotNull(content);
        Assert.Equal(mail, content!.Mail);
    }

    [Fact]
    public async Task Perfil_ConTokenExpirado_Devuelve401()
    {
        // Mismos Issuer/Audience/SigningKey que usa el pipeline de AddJwtBearer bajo el entorno
        // "Development" (appsettings.json + appsettings.Development.json, spec FEAT-001b Block 1),
        // pero con un TestTimeProvider cuyo reloj ya está 61 minutos en el pasado — emite un token
        // "ya vencido" sin esperar el plazo real de expiración (60 min).
        var jwtSettings = Options.Create(new JwtSettings
        {
            Issuer = "BingoCart",
            Audience = "BingoCart",
            SigningKey = "-uBhxtdhOe3nsTJGPrTVZP1EL16zOGI0VYCVTbY8Zr57",
            ExpirationMinutes = 60
        });
        var timeProvider = new TestTimeProvider(DateTimeOffset.UtcNow.AddMinutes(-61));
        var jwtTokenService = new JwtTokenService(jwtSettings, timeProvider);
        // spec FEAT-009a, Block 1: GenerarToken gana un tercer parámetro `rol`.
        var tokenExpirado = jwtTokenService.GenerarToken(Guid.NewGuid(), "expirado@example.com", "Organizador").Token;

        using var clienteSinCookiesAutomaticas = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/organizadores/perfil");
        request.Headers.Add("Cookie", $"bingocart_auth={tokenExpirado}");

        var response = await clienteSinCookiesAutomaticas.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

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
    /// Registra un organizador real vía <c>POST /api/organizadores/registro</c> + login (mismo
    /// mecanismo que <c>NuevoClienteAutenticadoAsync</c> de <c>BingosControllerTests</c>) y devuelve
    /// un cliente HTTPS ya autenticado junto con el <c>Guid</c> del organizador creado — necesario
    /// para el test end-to-end de FR-01/AC-01 que crea un bingo real vía <c>POST /api/bingos</c>.
    /// </summary>
    private async Task<(HttpClient Cliente, Guid OrganizadorId)> RegistrarYLoguearAsync(string nombreOrganizacion)
    {
        var mail = NuevoMail();
        var cuit = NuevoCuitValido();

        using var clienteAnonimo = _factory.CreateClient();
        var registro = await clienteAnonimo.PostAsJsonAsync(
            "/api/organizadores/registro",
            CrearBody(nombreOrganizacion: nombreOrganizacion, cuit: cuit, mail: mail));
        registro.EnsureSuccessStatusCode();

        var registroContent = await registro.Content.ReadFromJsonAsync<RegistrarOrganizadorResponse>(DeserializeOptions);
        _organizadorIdsCreados.Add(registroContent!.Id);

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var login = await client.PostAsJsonAsync("/api/organizadores/login", new { mail, password = PasswordValida });
        login.EnsureSuccessStatusCode();

        return (client, registroContent.Id);
    }

    /// <summary>
    /// Siembra un organizador (<see cref="ApplicationUser"/>) + un <see cref="Bingo"/> activo
    /// directo contra <see cref="AppDbContext"/> (mismo mecanismo que
    /// <c>DirectorioRepositoryTests.NuevoOrganizador</c>/<c>SeedBingoAsync</c> de
    /// <c>BingosControllerTests</c>), sin pasar por los endpoints HTTP — necesario porque FR-06
    /// impide más de un bingo activo por organizador a la vez, así que sembrar varios organizadores
    /// distintos de una vez es más simple/rápido que registrar y loguear cada uno.
    /// </summary>
    private async Task<Guid> SembrarOrganizadorConBingoActivoAsync(
        string nombreOrganizacion,
        DateTime fechaSorteoUtc,
        DateTime ahoraUtc,
        string? cuit = null,
        string? mail = null,
        string? telefono = null)
    {
        var id = Guid.NewGuid();
        var mailReal = mail ?? $"test-directorio-{id}@example.com";

        var organizador = new ApplicationUser
        {
            Id = id,
            UserName = mailReal,
            Email = mailReal,
            NombreOrganizacion = nombreOrganizacion,
            Cuit = cuit ?? id.ToString("N")[..11],
            Telefono = telefono ?? TelefonoValido,
        };
        var bingo = Bingo.Crear("Bingo de prueba", fechaSorteoUtc, 10, 100m, id, ahoraUtc);

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;
        await using var context = new AppDbContext(options);
        context.Users.Add(organizador);
        context.Bingos.Add(bingo);
        await context.SaveChangesAsync();

        _organizadorIdsCreados.Add(id);
        _mailsCreados.Add(mailReal);

        return id;
    }

    [Fact]
    public async Task Directorio_SinCookieDeAutenticacion_Devuelve200()
    {
        var response = await _client.GetAsync("/api/organizadores/directorio");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Directorio_ConDosOrganizadoresRegistradosYUnBingoCadaUno_Devuelve200ConAmbosOrdenadosPorFechaSorteoAscendente()
    {
        var (clienteUno, _) = await RegistrarYLoguearAsync("Club Uno Directorio");
        var (clienteDos, _) = await RegistrarYLoguearAsync("Club Dos Directorio");

        var bingoUno = await clienteUno.PostAsJsonAsync(
            "/api/bingos",
            CrearBodyBingo(nombreEvento: "Bingo Mas Lejano", fechaSorteoUtc: DateTime.UtcNow.AddDays(20)));
        Assert.Equal(HttpStatusCode.Created, bingoUno.StatusCode);

        var bingoDos = await clienteDos.PostAsJsonAsync(
            "/api/bingos",
            CrearBodyBingo(nombreEvento: "Bingo Mas Proximo", fechaSorteoUtc: DateTime.UtcNow.AddDays(5)));
        Assert.Equal(HttpStatusCode.Created, bingoDos.StatusCode);

        var response = await _client.GetAsync("/api/organizadores/directorio");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<DirectorioResponseDto>(DeserializeOptions);
        Assert.NotNull(content);

        var items = content!.Items.ToList();
        var indexProximo = items.FindIndex(i => i.NombreEvento == "Bingo Mas Proximo");
        var indexLejano = items.FindIndex(i => i.NombreEvento == "Bingo Mas Lejano");

        Assert.True(indexProximo >= 0, "El bingo del organizador Dos no aparece en el directorio.");
        Assert.True(indexLejano >= 0, "El bingo del organizador Uno no aparece en el directorio.");
        Assert.True(indexProximo < indexLejano, "El sorteo más próximo debería aparecer primero.");
        Assert.Equal("Club Dos Directorio", items[indexProximo].NombreOrganizacion);
        Assert.Equal("Club Uno Directorio", items[indexLejano].NombreOrganizacion);
    }

    [Fact]
    public async Task Directorio_ConSieteOrganizadoresConBingoActivoSembrados_Page2PageSize5_Devuelve200ConLosDosRestantesYTotalPaginasDos()
    {
        var ahoraUtc = DateTime.UtcNow;
        for (var i = 0; i < 7; i++)
        {
            await SembrarOrganizadorConBingoActivoAsync(
                $"Club Sembrado Directorio {i}", ahoraUtc.AddDays(1 + i), ahoraUtc);
        }

        var response = await _client.GetAsync("/api/organizadores/directorio?page=2&pageSize=5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<DirectorioResponseDto>(DeserializeOptions);
        Assert.NotNull(content);
        Assert.Equal(2, content!.Items.Count);
        Assert.Equal(7, content.Total);
        Assert.Equal(2, content.TotalPaginas);
        Assert.Equal(2, content.Page);
        Assert.Equal(5, content.PageSize);
        Assert.Equal("Club Sembrado Directorio 5", content.Items[0].NombreOrganizacion);
        Assert.Equal("Club Sembrado Directorio 6", content.Items[1].NombreOrganizacion);
    }

    [Fact]
    public async Task Directorio_ConPageCero_Devuelve400DatosInvalidos()
    {
        var response = await _client.GetAsync("/api/organizadores/directorio?page=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponseDto>(DeserializeOptions);
        Assert.NotNull(error);
        Assert.Equal("DatosInvalidos", error!.Error);
    }

    [Fact]
    public async Task Directorio_SinNingunOrganizadorConBingoActivo_Devuelve200ConItemsVacioYTotalCero()
    {
        var response = await _client.GetAsync("/api/organizadores/directorio");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<DirectorioResponseDto>(DeserializeOptions);
        Assert.NotNull(content);
        Assert.Empty(content!.Items);
        Assert.Equal(0, content.Total);
    }

    [Fact]
    public async Task Directorio_ConOrganizadorConCuitMailTelefono_LaRespuestaCrudaNoContieneEsosDatos()
    {
        var ahoraUtc = DateTime.UtcNow;
        var id = Guid.NewGuid();
        var mailSensible = $"sensible-directorio-{id}@example.com";
        var cuitSensible = id.ToString("N")[..11];
        var telefonoSensible = "+54 11 9999-8888";

        await SembrarOrganizadorConBingoActivoAsync(
            "Club Con Datos Sensibles",
            ahoraUtc.AddDays(3),
            ahoraUtc,
            cuit: cuitSensible,
            mail: mailSensible,
            telefono: telefonoSensible);

        var response = await _client.GetAsync("/api/organizadores/directorio");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rawBody = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(cuitSensible, rawBody);
        Assert.DoesNotContain(mailSensible, rawBody);
        Assert.DoesNotContain(telefonoSensible, rawBody);
    }

    [Fact]
    public async Task Directorio_Con30RequestsPreviasEnLaVentana_Request31Devuelve429()
    {
        for (var intento = 1; intento <= 30; intento++)
        {
            var respuesta = await _client.GetAsync("/api/organizadores/directorio");
            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        }

        var request31 = await _client.GetAsync("/api/organizadores/directorio");

        Assert.Equal(HttpStatusCode.TooManyRequests, request31.StatusCode);
    }

    private sealed record ErrorResponseDto(string Error, string Message);

    private sealed record PerfilOrganizadorResponseDto(string Mail);

    private sealed record DirectorioResponseDto(
        IReadOnlyList<DirectorioItemDto> Items, int Total, int TotalPaginas, int Page, int PageSize);

    private sealed record DirectorioItemDto(string NombreOrganizacion, string NombreEvento, DateTime FechaSorteoUtc);
}
