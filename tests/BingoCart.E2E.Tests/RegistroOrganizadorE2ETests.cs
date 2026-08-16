using BingoCart.Infrastructure.Data;
using BingoCart.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace BingoCart.E2E.Tests;

/// <summary>
/// E2E Playwright para .NET (Block 6, spec FEAT-001a) — AC-01 y AC-03. Maneja un navegador real
/// contra el frontend Angular servido en <c>http://localhost:8000</c> y la Api en
/// <c>http://localhost:8080</c>, ambos corriendo en vivo durante el test (ver README del bloque /
/// completion criterion). Cada test genera un mail único (<see cref="Guid.NewGuid"/>, Regla #0)
/// y borra el <see cref="ApplicationUser"/> que crea en el teardown, conectándose directamente a
/// <see cref="AppDbContext"/>.
/// </summary>
public sealed class RegistroOrganizadorE2ETests : IAsyncLifetime
{
    private const string FrontendUrl = "http://localhost:8000";

    // CUITs con dígito verificador válido según el algoritmo estándar CUIT/CUIL (mismo algoritmo
    // que BingoCart.Domain.Organizadores.CuitValidator) — el validador Angular del cliente solo
    // verifica el formato de 11 dígitos (Block 6), pero Domain sí valida el dígito verificador
    // (Block 2), así que un CUIT arbitrario de 11 dígitos haría fallar el registro con
    // CuitInvalido en vez de completar el flujo feliz.
    private const string CuitValidoA = "20304050609";
    private const string CuitValidoB = "20123456786";

    // Misma credencial de desarrollo local que src/BingoCart.Api/appsettings.Development.json
    // (Block 1) — únicamente para conectarse al mismo contenedor `db` dockerizado y limpiar los
    // datos que este test crea (Regla #0). Sobreescribible con la misma variable de entorno que
    // usa la Api (`ConnectionStrings__Default`) para no duplicar el secreto en un tercer lugar.
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("ConnectionStrings__Default")
        ?? "Server=localhost,14330;Database=BingoCart;User Id=sa;Password=BingoCart_Dev2026!;TrustServerCertificate=True;Encrypt=True;";

    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;
    private IPage _page = null!;
    private readonly List<string> _mailsACrear = new();

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        _page = await _browser.NewPageAsync();
    }

    public async Task DisposeAsync()
    {
        await _browser.CloseAsync();
        _playwright.Dispose();
        await LimpiarOrganizadoresCreadosAsync();
    }

    [Fact]
    public async Task FlujoFeliz_CompletaFormularioYRegistraOrganizador()
    {
        var mail = MailUnico();

        await CompletarYEnviarFormularioAsync(
            nombreOrganizacion: "Club Social E2E",
            cuit: CuitValidoA,
            mail: mail,
            telefono: "+54 11 4444-5555",
            password: "Abcdefg1!");

        await _page.Locator("[data-testid=mensaje-exito]").WaitForAsync();

        var visible = await _page.Locator("[data-testid=mensaje-exito]").IsVisibleAsync();
        Assert.True(visible, "Se esperaba el mensaje de éxito tras un registro válido (AC-01).");
    }

    [Fact]
    public async Task MailDuplicado_MuestraErrorEnElFormulario()
    {
        var mailDuplicado = MailUnico();

        await CompletarYEnviarFormularioAsync(
            nombreOrganizacion: "Club Social E2E - primero",
            cuit: CuitValidoA,
            mail: mailDuplicado,
            telefono: "+54 11 4444-5555",
            password: "Abcdefg1!");

        await _page.Locator("[data-testid=mensaje-exito]").WaitForAsync();

        await CompletarYEnviarFormularioAsync(
            nombreOrganizacion: "Club Social E2E - segundo",
            cuit: CuitValidoB,
            mail: mailDuplicado,
            telefono: "+54 11 6666-7777",
            password: "Zxcvbnm2!");

        await _page.Locator("[data-testid=error-mail-backend]").WaitForAsync();

        var visible = await _page.Locator("[data-testid=error-mail-backend]").IsVisibleAsync();
        Assert.True(visible, "Se esperaba el error de mail duplicado en el campo mail (AC-03).");
    }

    private async Task CompletarYEnviarFormularioAsync(
        string nombreOrganizacion, string cuit, string mail, string telefono, string password)
    {
        await _page.GotoAsync($"{FrontendUrl}/auth/registro");

        await _page.Locator("[data-testid=input-nombre]").FillAsync(nombreOrganizacion);
        await _page.Locator("[data-testid=input-cuit]").FillAsync(cuit);
        await _page.Locator("[data-testid=input-mail]").FillAsync(mail);
        await _page.Locator("[data-testid=input-telefono]").FillAsync(telefono);
        await _page.Locator("[data-testid=input-password]").FillAsync(password);

        await _page.Locator("[data-testid=boton-submit]").ClickAsync();
    }

    private string MailUnico()
    {
        var mail = $"e2e-{Guid.NewGuid()}@example.com";
        _mailsACrear.Add(mail);
        return mail;
    }

    private async Task LimpiarOrganizadoresCreadosAsync()
    {
        if (_mailsACrear.Count == 0)
        {
            return;
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        await using var dbContext = new AppDbContext(options);

        foreach (var mail in _mailsACrear)
        {
            var usuario = await dbContext.Users.SingleOrDefaultAsync(u => u.Email == mail);
            if (usuario is not null)
            {
                dbContext.Users.Remove(usuario);
            }
        }

        await dbContext.SaveChangesAsync();
    }
}
