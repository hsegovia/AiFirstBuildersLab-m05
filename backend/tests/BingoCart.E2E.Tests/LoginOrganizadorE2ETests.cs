using System.Net.Http.Json;
using BingoCart.Infrastructure.Data;
using BingoCart.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace BingoCart.E2E.Tests;

/// <summary>
/// E2E Playwright para .NET (spec FEAT-001b, Block 4) — cierra el loop de AC-01 a nivel frontend:
/// un login real desde el formulario deja fijada la cookie httpOnly <c>bingocart_auth</c>, nunca
/// accesible desde JavaScript. Mismo patrón que <see cref="RegistroOrganizadorE2ETests"/>: navegador
/// real contra el frontend Angular (<c>:8000</c>) y la Api (<c>:8080</c>), ambos corriendo en vivo
/// durante el test. El organizador de prueba se crea llamando directamente al endpoint de registro
/// (Block 1, FEAT-001a) por HTTP — más simple y confiable que repetir el flujo de UI de registro
/// solo para preparar datos de este test — y se borra en <see cref="DisposeAsync"/> (Regla #0).
/// </summary>
public sealed class LoginOrganizadorE2ETests : IAsyncLifetime
{
    private const string FrontendUrl = "http://localhost:8000";
    private const string ApiUrl = "http://localhost:8080";

    // CUIT con dígito verificador válido (mismo algoritmo que BingoCart.Domain.Organizadores.
    // CuitValidator) — ver comentario equivalente en RegistroOrganizadorE2ETests.
    private const string CuitValido = "20304050609";
    private const string PasswordValida = "Abcdefg1!";

    // Misma credencial de desarrollo local que backend/BingoCart.Api/appsettings.Development.json
    // — únicamente para conectarse al mismo contenedor `db` dockerizado y limpiar los datos que
    // este test crea (Regla #0).
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("ConnectionStrings__Default")
        ?? "Server=localhost,14330;Database=BingoCart;User Id=sa;Password=BingoCart_Dev2026!;TrustServerCertificate=True;Encrypt=True;";

    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;
    private IBrowserContext _context = null!;
    private IPage _page = null!;
    private readonly List<string> _mailsACrear = new();

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        _context = await _browser.NewContextAsync();
        _page = await _context.NewPageAsync();
    }

    public async Task DisposeAsync()
    {
        await _browser.CloseAsync();
        _playwright.Dispose();
        await LimpiarOrganizadoresCreadosAsync();
    }

    [Fact]
    public async Task LoginConCredencialesCorrectas_FijaCookieHttpOnlyBingocartAuth()
    {
        var mail = MailUnico();
        await RegistrarOrganizadorDePruebaAsync(mail, PasswordValida);

        await _page.GotoAsync($"{FrontendUrl}/auth/login");
        await _page.Locator("[data-testid=input-mail]").FillAsync(mail);
        await _page.Locator("[data-testid=input-password]").FillAsync(PasswordValida);
        await _page.Locator("[data-testid=boton-submit]").ClickAsync();

        // Tras un login exitoso el componente redirige a "/" (Block 4) — esperamos esa navegación
        // para dar tiempo a que el navegador haya procesado el header Set-Cookie de la respuesta.
        await _page.WaitForURLAsync($"{FrontendUrl}/");

        var cookies = await _context.CookiesAsync();
        var cookieAuth = cookies.SingleOrDefault(c => c.Name == "bingocart_auth");

        Assert.True(cookieAuth is not null, "Se esperaba que el login fijara la cookie bingocart_auth (AC-01).");
        Assert.True(cookieAuth!.HttpOnly, "La cookie bingocart_auth debe ser httpOnly (no accesible desde JS).");
    }

    private async Task RegistrarOrganizadorDePruebaAsync(string mail, string password)
    {
        using var httpClient = new HttpClient { BaseAddress = new Uri(ApiUrl) };

        var response = await httpClient.PostAsJsonAsync("/api/organizadores/registro", new
        {
            nombreOrganizacion = "Club Social E2E Login",
            cuit = CuitValido,
            mail,
            telefono = "+54 11 4444-5555",
            password,
        });

        response.EnsureSuccessStatusCode();
    }

    private string MailUnico()
    {
        var mail = $"e2e-login-{Guid.NewGuid()}@example.com";
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
