using BingoCart.Application.Auth;
using BingoCart.Application.Compradores;
using BingoCart.Application.Compradores.Dtos;
using BingoCart.Application.Organizadores;
using BingoCart.Domain.Auth.Exceptions;
using BingoCart.Domain.Compradores.Exceptions;
using Moq;

namespace BingoCart.Application.Tests.Compradores;

/// <summary>
/// Tests unitarios de <see cref="CompradorService"/> (spec FEAT-009a, Block 2) — mock de
/// <see cref="ICompradorIdentityGateway"/>, mismo patrón que <c>OrganizadorServiceTests</c>.
/// </summary>
public class CompradorServiceTests
{
    // Mismo CUIT válido conocido usado en BingoCart.Domain.Tests.Compradores.CompradorTests.
    private const string CuitValido = "30500010912";
    private const string PasswordValida = "Passw0rd!";

    private static RegistrarCompradorRequest CrearRequest(
        string apellido = "Pérez",
        string nombre = "Juan",
        string cuit = CuitValido,
        string mail = "juan.perez@example.com",
        string password = PasswordValida)
    {
        return new RegistrarCompradorRequest(apellido, nombre, cuit, mail, password);
    }

    private static CompradorService CrearService(
        Mock<ICompradorIdentityGateway> gateway,
        Mock<IJwtTokenService>? jwtTokenService = null)
    {
        return new CompradorService(gateway.Object, (jwtTokenService ?? new Mock<IJwtTokenService>()).Object);
    }

    [Fact]
    public async Task RegistrarAsync_ConCuitValidoYMailNoExistente_InvocaCrearUsuarioAsync()
    {
        var gateway = new Mock<ICompradorIdentityGateway>();
        gateway.Setup(g => g.ExisteMailAsync(It.IsAny<string>())).ReturnsAsync(false);
        gateway
            .Setup(g => g.CrearUsuarioAsync(It.IsAny<Domain.Compradores.Comprador>(), It.IsAny<string>()))
            .ReturnsAsync(new IdentityGatewayResult(true, Array.Empty<string>()));

        var service = CrearService(gateway);

        var response = await service.RegistrarAsync(CrearRequest());

        Assert.NotEqual(Guid.Empty, response.Id);
        Assert.Equal("Pérez", response.Apellido);
        Assert.Equal("Juan", response.Nombre);
        Assert.Equal("juan.perez@example.com", response.Mail);
        gateway.Verify(
            g => g.CrearUsuarioAsync(It.IsAny<Domain.Compradores.Comprador>(), PasswordValida),
            Times.Once());
    }

    [Fact]
    public async Task RegistrarAsync_ConCuitInvalido_LanzaExcepcionYNoInvocaAlGateway()
    {
        var gateway = new Mock<ICompradorIdentityGateway>();
        var service = CrearService(gateway);

        await Assert.ThrowsAsync<CuitInvalidoException>(() =>
            service.RegistrarAsync(CrearRequest(cuit: "12345")));

        gateway.Verify(g => g.ExisteMailAsync(It.IsAny<string>()), Times.Never());
        gateway.Verify(
            g => g.CrearUsuarioAsync(It.IsAny<Domain.Compradores.Comprador>(), It.IsAny<string>()),
            Times.Never());
    }

    [Fact]
    public async Task RegistrarAsync_ConMailYaExistente_LanzaMailYaRegistradoExceptionYNoInvocaCrearUsuarioAsync()
    {
        var gateway = new Mock<ICompradorIdentityGateway>();
        gateway.Setup(g => g.ExisteMailAsync(It.IsAny<string>())).ReturnsAsync(true);

        var service = CrearService(gateway);

        await Assert.ThrowsAsync<MailYaRegistradoException>(() =>
            service.RegistrarAsync(CrearRequest()));

        gateway.Verify(
            g => g.CrearUsuarioAsync(It.IsAny<Domain.Compradores.Comprador>(), It.IsAny<string>()),
            Times.Never());
    }

    [Fact]
    public async Task RegistrarAsync_ConPasswordQueNoCumpleLaPolitica_LanzaPasswordInvalidaException()
    {
        var errores = new List<string> { "Se requiere al menos un dígito." };

        var gateway = new Mock<ICompradorIdentityGateway>();
        gateway.Setup(g => g.ExisteMailAsync(It.IsAny<string>())).ReturnsAsync(false);
        gateway
            .Setup(g => g.CrearUsuarioAsync(It.IsAny<Domain.Compradores.Comprador>(), It.IsAny<string>()))
            .ReturnsAsync(new IdentityGatewayResult(false, errores));

        var service = CrearService(gateway);

        var ex = await Assert.ThrowsAsync<PasswordInvalidaException>(() =>
            service.RegistrarAsync(CrearRequest(password: "abc")));

        Assert.Contains("Se requiere al menos un dígito.", ex.Message);
        Assert.DoesNotContain("abc", ex.Message);
    }

    [Fact]
    public async Task AutenticarAsync_ConCredencialesValidas_DevuelveResponseConJwtDeRolComprador()
    {
        var compradorId = Guid.NewGuid();
        const string mail = "juan.perez@example.com";

        var gateway = new Mock<ICompradorIdentityGateway>();
        gateway.Setup(g => g.AutenticarAsync(mail, PasswordValida))
            .ReturnsAsync(new ResultadoAutenticacion(EstadoAutenticacion.Exitoso, compradorId));

        var expiraEnUtc = new DateTime(2026, 1, 1, 1, 0, 0, DateTimeKind.Utc);
        var jwtTokenService = new Mock<IJwtTokenService>();
        jwtTokenService.Setup(j => j.GenerarToken(compradorId, mail, "Comprador"))
            .Returns(new TokenGenerado("token-jwt-de-prueba", expiraEnUtc));

        var service = CrearService(gateway, jwtTokenService);

        var response = await service.AutenticarAsync(new LoginCompradorRequest(mail, PasswordValida));

        Assert.Equal("token-jwt-de-prueba", response.Token);
        Assert.Equal(expiraEnUtc, response.ExpiraEnUtc);
        jwtTokenService.Verify(j => j.GenerarToken(compradorId, mail, "Comprador"), Times.Once());
    }

    [Fact]
    public async Task AutenticarAsync_ConCredencialesInvalidas_LanzaCredencialesInvalidasException()
    {
        var gateway = new Mock<ICompradorIdentityGateway>();
        gateway.Setup(g => g.AutenticarAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new ResultadoAutenticacion(EstadoAutenticacion.CredencialesInvalidas, null));

        var service = CrearService(gateway);

        await Assert.ThrowsAsync<CredencialesInvalidasException>(() =>
            service.AutenticarAsync(new LoginCompradorRequest("inexistente@example.com", "cualquiera")));
    }
}
