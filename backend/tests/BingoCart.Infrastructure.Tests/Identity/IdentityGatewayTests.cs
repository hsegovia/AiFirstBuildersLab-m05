using System;
using System.Threading.Tasks;
using BingoCart.Infrastructure.Data;
using BingoCart.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BingoCart.Infrastructure.Tests.Identity;

/// <summary>
/// Tests de integración de <see cref="IdentityGateway"/> (spec FEAT-009d, Block 5) contra SQL
/// Server real — mismo patrón que <c>BingoRepositoryTests</c>: base propia y descartable
/// (<c>BingoCartTests_IdentityGateway</c>), migrada al inicio y eliminada en
/// <see cref="DisposeAsync"/> (Rule #0 de testing.instructions.md). A diferencia de los demás tests
/// de este proyecto, acá se necesita un <see cref="UserManager{TUser}"/>/
/// <see cref="SignInManager{TUser}"/> reales (no se pueden mockear: son clases selladas de
/// Identity), así que se levanta el mismo <c>AddIdentity&lt;...&gt;().AddEntityFrameworkStores&lt;
/// AppDbContext&gt;()</c> que <c>Program.cs</c>, en un <see cref="ServiceCollection"/> propio y
/// aislado — sin levantar un host completo.
/// </summary>
public sealed class IdentityGatewayTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Server=localhost,14330;Database=BingoCartTests_IdentityGateway;User Id=sa;" +
        "Password=BingoCart_Dev2026!;TrustServerCertificate=True;Encrypt=True;";

    private const string PasswordValida = "Passw0rd!";

    private AppDbContext _context = null!;
    private ServiceProvider _serviceProvider = null!;
    private IdentityGateway _gateway = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        _context = new AppDbContext(options);
        await _context.Database.MigrateAsync();

        var services = new ServiceCollection();
        services.AddSingleton(_context);
        services.AddLogging();
        services.AddHttpContextAccessor();
        services
            .AddIdentity<ApplicationUser, IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>()
            .ConfigurarPoliticaDePassword();

        _serviceProvider = services.BuildServiceProvider();

        // CrearUsuarioAsync (usado por SembrarCompradorAsync) asigna el rol "Comprador" — en
        // Program.cs ese rol ya está sembrado idempotentemente al arrancar; acá, sin host, hay que
        // sembrarlo a mano antes de crear cualquier comprador de prueba.
        var roleManager = _serviceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        await roleManager.CreateAsync(new IdentityRole<Guid>("Comprador"));

        var userManager = _serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var signInManager = _serviceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
        _gateway = new IdentityGateway(userManager, signInManager);
    }

    public async Task DisposeAsync()
    {
        await _context.Database.EnsureDeletedAsync();
        await _context.DisposeAsync();
        await _serviceProvider.DisposeAsync();
    }

    private async Task<ApplicationUser> SembrarCompradorAsync(string mail, string cuit)
    {
        var comprador = BingoCart.Domain.Compradores.Comprador.Crear("Pérez", "Juan", cuit, mail);
        var resultado = await _gateway.CrearUsuarioAsync(comprador, PasswordValida);
        Assert.True(resultado.Exitoso, string.Join("; ", resultado.Errores));

        return await _context.Users.SingleAsync(u => u.Id == comprador.Id);
    }

    [Fact]
    public async Task ActualizarDatos_ActualizaUserNameYSecurityStampAdemasDeEmail()
    {
        const string mailViejo = "cuenta.vieja@example.com";
        const string mailNuevo = "cuenta.nueva@example.com";
        var usuario = await SembrarCompradorAsync(mailViejo, "30500010912");
        var securityStampViejo = usuario.SecurityStamp;

        var resultado = await _gateway.ActualizarDatosAsync(
            usuario.Id, "Gómez", "María", "30500010912", mailNuevo);

        Assert.True(resultado.Exitoso, string.Join("; ", resultado.Errores));

        // DbContext NUEVO (no el mismo _context) para leer la fila realmente persistida en SQL
        // Server, no del identity map en memoria — mismo criterio que CompraRepositoryTests.
        var optionsNuevo = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;
        await using var contextoNuevo = new AppDbContext(optionsNuevo);
        var usuarioPersistido = await contextoNuevo.Users.SingleAsync(u => u.Id == usuario.Id);

        // La parte que rompe si SetEmailAsync se usa a solas (R-03): Email/NormalizedEmail SÍ
        // cambian, pero UserName/NormalizedUserName NO, a menos que se toquen explícitamente.
        Assert.Equal(mailNuevo, usuarioPersistido.Email);
        Assert.Equal(mailNuevo.ToUpperInvariant(), usuarioPersistido.NormalizedEmail);
        Assert.Equal(mailNuevo, usuarioPersistido.UserName);
        Assert.Equal(mailNuevo.ToUpperInvariant(), usuarioPersistido.NormalizedUserName);
        Assert.NotEqual(securityStampViejo, usuarioPersistido.SecurityStamp);
    }
}
