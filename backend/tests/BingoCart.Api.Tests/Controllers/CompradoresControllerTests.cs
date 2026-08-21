using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BingoCart.Application.Compradores.Dtos;
using BingoCart.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace BingoCart.Api.Tests.Controllers;

/// <summary>
/// Tests de integración de <c>CompradoresController</c> (spec FEAT-009a, Block 3) contra el stack
/// completo levantado en memoria (<see cref="WebApplicationFactory{T}"/>) y el SQL Server real
/// dockerizado (puerto 14330) — mismo patrón que <c>OrganizadoresControllerTests</c>: cada test crea
/// su propio mail único (Rule #0 de testing.instructions.md) y lo elimina en
/// <see cref="DisposeAsync"/>. El CUIT usa un generador propio con semilla aleatoria (en vez del
/// contador secuencial de <c>OrganizadoresControllerTests</c>/<c>BingosControllerTests</c>) porque
/// <c>AspNetUsers.Cuit</c> comparte el mismo índice único entre organizador y comprador (decisión de
/// PLAN, spec FEAT-009a): con dos clases de test corriendo en paralelo (xUnit, default) y un
/// contador que arranca en 0 en cada una, la primera llamada de cada clase generaría el MISMO CUIT.
/// </summary>
public sealed class CompradoresControllerTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Server=localhost,14330;Database=BingoCart;User Id=sa;" +
        "Password=BingoCart_Dev2026!;TrustServerCertificate=True;Encrypt=True;";

    private const string PasswordValida = "Passw0rd!";

    private static readonly int[] MultiplicadoresCuit = { 5, 4, 3, 2, 7, 6, 5, 4, 3, 2 };

    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly List<string> _mailsCreados = new();

    public CompradoresControllerTests()
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

    private string NuevoMail()
    {
        var mail = $"test-comprador-{Guid.NewGuid()}@example.com";
        _mailsCreados.Add(mail);
        return mail;
    }

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
            mail = mail ?? $"test-comprador-{Guid.NewGuid()}@example.com",
            password,
        };
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
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        Assert.Contains(jwt.Claims, c => c.Type == System.Security.Claims.ClaimTypes.Role && c.Value == "Comprador");
    }

    private sealed record ErrorResponseDto(string Error, string Message);
}
