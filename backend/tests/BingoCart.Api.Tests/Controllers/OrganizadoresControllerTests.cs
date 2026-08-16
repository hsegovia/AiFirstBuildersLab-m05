using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BingoCart.Application.Organizadores.Dtos;
using BingoCart.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

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

    private sealed record ErrorResponseDto(string Error, string Message);
}
