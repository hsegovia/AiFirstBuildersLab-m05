using System.Net.Http.Json;
using BingoCart.Infrastructure.Data;
using BingoCart.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace BingoCart.E2E.Tests;

/// <summary>
/// E2E Playwright para .NET (spec FEAT-010a, Block 6) — cubre los comportamientos que un test
/// unitario de Angular (<c>TestBed</c>) no puede verificar porque dependen de un navegador real,
/// una cookie <c>httpOnly</c> real y una recarga de página real: AC-04 (refresh restaura la
/// sesión), AC-05 (refresh/navegación sin cookie válida en una ruta protegida redirige a login
/// preservando la ruta de destino) y AC-09 (el guard de rol redirige sin llamar a la API del rol
/// equivocado). Mismo patrón que <see cref="LoginOrganizadorE2ETests"/>/<see
/// cref="RegistroOrganizadorE2ETests"/>: navegador real contra el frontend Angular (<c>:8000</c>)
/// y la Api (<c>:8080</c>), ambos corriendo en vivo durante el test.
///
/// <para>
/// <b>Rutas protegidas placeholder (decisión de alcance del Block 6):</b> al momento de este
/// bloque no existe ninguna ruta real de organizador/comprador en <c>app-routing.module.ts</c> —
/// las agregan los sub-tickets FEAT-010b/c/d/e. Sin al menos una ruta protegida real, los guards
/// (Block 4) no tienen nada que proteger en el router de producción. Se agregaron dos rutas
/// placeholder MÍNIMAS y explícitamente marcadas como TEMPORALES en <c>app-routing.module.ts</c>
/// (<c>/placeholder-organizador-e2e-010a</c> y <c>/placeholder-comprador-e2e-010a</c>, sin el
/// prefijo <c>organizador/</c>/<c>comprador/</c> a propósito — ver el comentario de esas rutas),
/// que reutilizan <c>HomeComponent</c> (puramente presentacional, sin llamadas HTTP propias) solo
/// para que <c>organizadorGuard</c>/<c>compradorGuard</c> tengan algo que proteger. Deben
/// eliminarse, junto con los dos tests de este archivo que las navegan directamente, en cuanto
/// FEAT-010b/c/d/e agreguen las rutas reales.
/// </para>
/// </summary>
public sealed class SesionYGuardsE2ETests : IAsyncLifetime
{
    private const string FrontendUrl = "http://localhost:8000";
    private const string ApiUrl = "http://localhost:8080";

    // Rutas placeholder temporales — ver comentario de clase.
    private const string RutaProtegidaOrganizador = "/placeholder-organizador-e2e-010a";
    private const string RutaProtegidaComprador = "/placeholder-comprador-e2e-010a";

    // CUITs con dígito verificador válido (mismo algoritmo que BingoCart.Domain.Organizadores.
    // CuitValidator). Se usan dos porque el dominio exige unicidad de CUIT: los tests 1 y 3 registran
    // cada uno un organizador, y si ambos usaran el mismo CUIT el segundo registro fallaría —
    // mismo patrón que CuitValidoA/CuitValidoB en RegistroOrganizadorE2ETests.
    private const string CuitValidoA = "20304050609";
    private const string CuitValidoB = "20123456786";
    private const string PasswordValida = "Abcdefg1!";

    // Misma credencial de desarrollo local que backend/BingoCart.Api/appsettings.Development.json
    // — únicamente para conectarse al mismo contenedor `db` dockerizado y limpiar los datos que
    // este test crea (Regla #0).
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("ConnectionStrings__Default")
        ?? "Server=localhost,14330;Database=BingoCart;User Id=sa;Password=BingoCart_Dev2026!;TrustServerCertificate=True;Encrypt=True;";

    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;
    private readonly List<string> _mailsACrear = new();

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    public async Task DisposeAsync()
    {
        await _browser.CloseAsync();
        _playwright.Dispose();
        await LimpiarOrganizadoresCreadosAsync();
    }

    [Fact]
    public async Task TrasLoginDeOrganizadorYRefresh_MantieneLaSesionYElLayout()
    {
        await using var context = await _browser.NewContextAsync();
        var page = await context.NewPageAsync();

        var mail = MailUnico();
        await RegistrarOrganizadorDePruebaAsync(CuitValidoA, mail, PasswordValida);
        await LoguearOrganizadorAsync(page, mail, PasswordValida);

        // Sesión ya vigente en "/" tras el login (nav de organizador visible, AC-03/AC-04 previos
        // a este bloque). El punto real del test es que sobreviva a un refresh real de página, no
        // a una navegación SPA — un refresh reconstruye toda la app desde cero (APP_INITIALIZER).
        await page.ReloadAsync();

        await page.Locator("[data-testid=nav-mis-bingos]").WaitForAsync();
        var navOrganizadorVisible = await page.Locator("[data-testid=nav-mis-bingos]").IsVisibleAsync();
        var navAnonimaPresente = await page.Locator("[data-testid=nav-login-organizador]").CountAsync();

        Assert.True(
            navOrganizadorVisible,
            "Tras el refresh se esperaba que el layout siguiera mostrando la nav de organizador (AC-04).");
        Assert.Equal(0, navAnonimaPresente);
        Assert.Equal($"{FrontendUrl}/", page.Url);
    }

    [Fact]
    public async Task SinCookieEnUnaRutaProtegida_RedirigeALoginPreservandoLaRutaDeDestino()
    {
        // Contexto de navegador nuevo y sin cookies (Regla #0/aislamiento) — nunca hubo login.
        await using var context = await _browser.NewContextAsync();
        var page = await context.NewPageAsync();

        // Solo se vigilan errores no manejados de JavaScript (excepciones del bundle Angular) —
        // no cualquier mensaje de consola tipo "error", que también captura ruido no relacionado
        // (ej. un 404 de favicon.ico) y haría el assert frágil sin relación con el comportamiento
        // del guard.
        var erroresDePagina = new List<string>();
        page.PageError += (_, mensaje) => erroresDePagina.Add(mensaje);

        await page.GotoAsync($"{FrontendUrl}{RutaProtegidaOrganizador}");
        await page.WaitForURLAsync(url => url.Contains("/auth/login"));

        var uri = new Uri(page.Url);
        var returnUrl = System.Web.HttpUtility.ParseQueryString(uri.Query).Get("returnUrl");

        Assert.StartsWith($"{FrontendUrl}/auth/login", page.Url);
        Assert.Equal(RutaProtegidaOrganizador, returnUrl);
        Assert.Empty(erroresDePagina);
    }

    [Fact]
    public async Task ConRolOrganizadorEnUnaRutaDeComprador_RedirigeSinLlamarALaApiDeComprador()
    {
        await using var context = await _browser.NewContextAsync();
        var page = await context.NewPageAsync();

        var mail = MailUnico();
        await RegistrarOrganizadorDePruebaAsync(CuitValidoB, mail, PasswordValida);
        await LoguearOrganizadorAsync(page, mail, PasswordValida);

        var requestsRegistradas = new List<string>();
        page.Request += (_, request) => requestsRegistradas.Add(request.Url);

        await page.GotoAsync($"{FrontendUrl}{RutaProtegidaComprador}");
        await page.WaitForURLAsync(url => url.Contains("/auth/login"));

        // El guard corta la navegación ANTES de montar el componente de la ruta de comprador: no
        // debe haber ninguna request de red hacia un endpoint de comprador (AC-09). Se inspecciona
        // el log completo de requests de Playwright capturado durante esta navegación puntual —
        // Page.Request se dispara para toda request de red de la página, incluidas las XHR/fetch
        // del interceptor HTTP de Angular.
        var llamoALaApiDeComprador = requestsRegistradas.Any(url =>
            url.Contains("/api/comprador", StringComparison.OrdinalIgnoreCase));

        Assert.False(
            llamoALaApiDeComprador,
            "El guard debe redirigir sin que el componente de la ruta de comprador llegue a llamar a su API (AC-09).");

        // La ruta placeholder (ver comentario de clase) no lleva el prefijo `comprador/`, así que
        // `rolEsperadoPorRuta` cae al default (Organizador) y el destino es `/auth/login` (la
        // única pantalla de login que existe hoy) — `/auth/login-comprador` no es todavía una ruta
        // real (la agrega FEAT-010d). Lo que importa para AC-09 es que hubo una redirección real
        // fuera de la ruta protegida, con la ruta de destino preservada, sin la llamada a la API.
        var uri = new Uri(page.Url);
        var returnUrl = System.Web.HttpUtility.ParseQueryString(uri.Query).Get("returnUrl");

        Assert.StartsWith($"{FrontendUrl}/auth/login", page.Url);
        Assert.Equal(RutaProtegidaComprador, returnUrl);
    }

    private static async Task LoguearOrganizadorAsync(IPage page, string mail, string password)
    {
        await page.GotoAsync($"{FrontendUrl}/auth/login");
        await page.Locator("[data-testid=input-mail]").FillAsync(mail);
        await page.Locator("[data-testid=input-password]").FillAsync(password);
        await page.Locator("[data-testid=boton-submit]").ClickAsync();
        await page.WaitForURLAsync($"{FrontendUrl}/");
    }

    private async Task RegistrarOrganizadorDePruebaAsync(string cuit, string mail, string password)
    {
        using var httpClient = new HttpClient { BaseAddress = new Uri(ApiUrl) };

        var response = await httpClient.PostAsJsonAsync("/api/organizadores/registro", new
        {
            nombreOrganizacion = "Club Social E2E Sesion",
            cuit,
            mail,
            telefono = "+54 11 4444-5555",
            password,
        });

        response.EnsureSuccessStatusCode();
    }

    private string MailUnico()
    {
        var mail = $"e2e-sesion-{Guid.NewGuid()}@example.com";
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
