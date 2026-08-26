using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

using BingoCart.Infrastructure.Data;

namespace BingoCart.Api.Tests.Controllers;

/// <summary>
/// Tests de integración de <c>GET /api/auth/whoami</c> (spec FEAT-010a, Block 1) contra el stack
/// completo levantado en memoria (<see cref="WebApplicationFactory{T}"/>) y el SQL Server real
/// dockerizado (puerto 14330) — mismo patrón que <c>OrganizadoresControllerTests.Perfil_*</c>: la
/// cookie <c>bingocart_auth</c> es <c>Secure</c>, así que todo cliente autenticado usa
/// <c>BaseAddress = https://localhost</c>.
/// </summary>
public sealed class AuthControllerTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Server=localhost,14330;Database=BingoCart;User Id=sa;" +
        "Password=BingoCart_Dev2026!;TrustServerCertificate=True;Encrypt=True;";

    private const string CuitValido = "30500010912";
    private const string TelefonoValido = "+54 11 4444-5555";
    private const string PasswordValida = "Passw0rd!";

    private static readonly int[] MultiplicadoresCuit = { 5, 4, 3, 2, 7, 6, 5, 4, 3, 2 };

    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly WebApplicationFactory<Program> _factory;
    private readonly List<string> _mailsCreados = new();

    public AuthControllerTests()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
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

        await _factory.DisposeAsync();
    }

    // Semilla aleatoria, mismo criterio que MisCartonesControllerTests: evita colisión del índice
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

    private string NuevoMail(string prefijo)
    {
        var mail = $"test-{prefijo}-{Guid.NewGuid()}@example.com";
        _mailsCreados.Add(mail);
        return mail;
    }

    /// <summary>
    /// Registra y loguea un organizador real vía los endpoints HTTP de
    /// <c>OrganizadoresController</c>, devolviendo un cliente HTTPS ya con la cookie
    /// <c>bingocart_auth</c> (rol <c>Organizador</c>) y el mail usado.
    /// </summary>
    private async Task<(HttpClient Client, string Mail)> NuevoOrganizadorAutenticadoAsync()
    {
        var mail = NuevoMail("auth-org");
        var client = NuevoClienteHttps();

        var registro = await client.PostAsJsonAsync("/api/organizadores/registro", new
        {
            nombreOrganizacion = "Club Auth Whoami",
            cuit = CuitValido,
            mail,
            telefono = TelefonoValido,
            password = PasswordValida,
        });
        registro.EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync("/api/organizadores/login", new { mail, password = PasswordValida });
        login.EnsureSuccessStatusCode();

        return (client, mail);
    }

    /// <summary>
    /// Registra y loguea un comprador real vía los endpoints HTTP de <c>CompradoresController</c>,
    /// devolviendo un cliente HTTPS ya con la cookie <c>bingocart_auth</c> (rol <c>Comprador</c>) y
    /// el mail usado.
    /// </summary>
    private async Task<(HttpClient Client, string Mail)> NuevoCompradorAutenticadoAsync()
    {
        var mail = NuevoMail("auth-comprador");
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

        return (client, mail);
    }

    [Fact]
    public async Task WhoAmI_ConSesionDeOrganizador_DevuelveRolYMail()
    {
        var (client, mail) = await NuevoOrganizadorAutenticadoAsync();

        var respuesta = await client.GetAsync("/api/auth/whoami");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var body = await respuesta.Content.ReadFromJsonAsync<WhoAmIResponseDto>(DeserializeOptions);
        Assert.NotNull(body);
        Assert.Equal("Organizador", body!.Rol);
        Assert.Equal(mail, body.Mail);
    }

    [Fact]
    public async Task WhoAmI_ConSesionDeComprador_DevuelveRolYMail()
    {
        var (client, mail) = await NuevoCompradorAutenticadoAsync();

        var respuesta = await client.GetAsync("/api/auth/whoami");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var body = await respuesta.Content.ReadFromJsonAsync<WhoAmIResponseDto>(DeserializeOptions);
        Assert.NotNull(body);
        Assert.Equal("Comprador", body!.Rol);
        Assert.Equal(mail, body.Mail);
    }

    // Mitigación R-01 (HIGH) del threat model FEAT-010a: parsea el JSON crudo (no el objeto
    // deserializado a WhoAmIResponse, que ocultaría un campo extra) y confirma que el set de
    // propiedades es EXACTAMENTE {Rol, Mail}, ni más ni menos — nunca NameIdentifier, nunca el JWT.
    [Fact]
    public async Task WhoAmI_LaRespuestaNoIncluyeNingunOtroCampo()
    {
        var (client, _) = await NuevoOrganizadorAutenticadoAsync();

        var respuesta = await client.GetAsync("/api/auth/whoami");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var rawBody = await respuesta.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(rawBody);
        var propiedades = json.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();

        // System.Text.Json serializa en camelCase por defecto en este proyecto (mismo criterio que
        // "expiraEnUtc" en LoginResponse) — el nombre en PascalCase del record C# no es el nombre
        // real de la propiedad en el JSON de la wire.
        Assert.Equal(new HashSet<string> { "rol", "mail" }, propiedades);
    }

    [Fact]
    public async Task WhoAmI_SinAutenticar_Devuelve401()
    {
        using var client = NuevoClienteHttps();

        var respuesta = await client.GetAsync("/api/auth/whoami");

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task WhoAmI_SuperandoElLimiteDeSolicitudes_Devuelve429()
    {
        var (client, _) = await NuevoOrganizadorAutenticadoAsync();

        // Política "auth-whoami": 60 permits / 1 min, particionada por el claim NameIdentifier.
        for (var intento = 1; intento <= 60; intento++)
        {
            var respuesta = await client.GetAsync("/api/auth/whoami");
            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        }

        var request61 = await client.GetAsync("/api/auth/whoami");

        Assert.Equal(HttpStatusCode.TooManyRequests, request61.StatusCode);
    }

    private sealed record WhoAmIResponseDto(string Rol, string Mail);
}
