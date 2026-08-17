using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BingoCart.Application.Auth;
using BingoCart.Application.Organizadores.Dtos;
using BingoCart.Infrastructure.Auth;
using BingoCart.Infrastructure.Data;
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

    public OrganizadoresControllerTests()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        _client = _factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_mailsCreados.Count > 0)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(ConnectionString)
                .Options;

            await using var context = new AppDbContext(options);
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
        var tokenExpirado = jwtTokenService.GenerarToken(Guid.NewGuid(), "expirado@example.com").Token;

        using var clienteSinCookiesAutomaticas = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/organizadores/perfil");
        request.Headers.Add("Cookie", $"bingocart_auth={tokenExpirado}");

        var response = await clienteSinCookiesAutomaticas.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record ErrorResponseDto(string Error, string Message);

    private sealed record PerfilOrganizadorResponseDto(string Mail);
}
