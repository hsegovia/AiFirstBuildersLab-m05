using BingoCart.Application.Organizadores;
using BingoCart.Application.Organizadores.Dtos;
using BingoCart.Domain.Organizadores.Exceptions;
using Moq;

namespace BingoCart.Application.Tests.Organizadores;

public class OrganizadorServiceTests
{
    // Mismo CUIT válido conocido usado en BingoCart.Domain.Tests.Organizadores.OrganizadorTests.
    private const string CuitValido = "30500010912";
    private const string TelefonoValido = "+54 11 4444-5555";
    private const string PasswordValida = "Passw0rd!";

    private static RegistrarOrganizadorRequest CrearRequest(
        string nombreOrganizacion = "Club Social y Deportivo",
        string cuit = CuitValido,
        string mail = "contacto@club.org",
        string telefono = TelefonoValido,
        string password = PasswordValida)
    {
        return new RegistrarOrganizadorRequest(nombreOrganizacion, cuit, mail, telefono, password);
    }

    [Fact]
    public async Task RegistrarAsync_ConDatosValidos_DevuelveResponseCorrecto()
    {
        var gateway = new Mock<IIdentityGateway>();
        gateway.Setup(g => g.ExisteMailAsync(It.IsAny<string>())).ReturnsAsync(false);
        gateway.Setup(g => g.CrearUsuarioAsync(It.IsAny<Domain.Organizadores.Organizador>(), It.IsAny<string>()))
            .ReturnsAsync(new IdentityGatewayResult(true, Array.Empty<string>()));

        var service = new OrganizadorService(gateway.Object);

        var response = await service.RegistrarAsync(CrearRequest());

        Assert.NotEqual(Guid.Empty, response.Id);
        Assert.Equal("Club Social y Deportivo", response.NombreOrganizacion);
        Assert.Equal("contacto@club.org", response.Mail);
    }

    [Fact]
    public async Task RegistrarAsync_ConCuitInvalido_LanzaExcepcionYNoLlamaAlGateway()
    {
        var gateway = new Mock<IIdentityGateway>();
        var service = new OrganizadorService(gateway.Object);

        await Assert.ThrowsAsync<CuitInvalidoException>(() =>
            service.RegistrarAsync(CrearRequest(cuit: "12345")));

        gateway.Verify(g => g.ExisteMailAsync(It.IsAny<string>()), Times.Never());
        gateway.Verify(
            g => g.CrearUsuarioAsync(It.IsAny<Domain.Organizadores.Organizador>(), It.IsAny<string>()),
            Times.Never());
    }

    [Fact]
    public async Task RegistrarAsync_ConMailDuplicado_LanzaMailYaRegistradoException()
    {
        var gateway = new Mock<IIdentityGateway>();
        gateway.Setup(g => g.ExisteMailAsync(It.IsAny<string>())).ReturnsAsync(true);

        var service = new OrganizadorService(gateway.Object);

        await Assert.ThrowsAsync<MailYaRegistradoException>(() =>
            service.RegistrarAsync(CrearRequest()));

        gateway.Verify(
            g => g.CrearUsuarioAsync(It.IsAny<Domain.Organizadores.Organizador>(), It.IsAny<string>()),
            Times.Never());
    }

    [Fact]
    public async Task RegistrarAsync_ConPasswordInvalida_LanzaPasswordInvalidaException()
    {
        var errores = new List<string> { "Se requiere al menos un dígito.", "Se requiere al menos 8 caracteres." };

        var gateway = new Mock<IIdentityGateway>();
        gateway.Setup(g => g.ExisteMailAsync(It.IsAny<string>())).ReturnsAsync(false);
        gateway.Setup(g => g.CrearUsuarioAsync(It.IsAny<Domain.Organizadores.Organizador>(), It.IsAny<string>()))
            .ReturnsAsync(new IdentityGatewayResult(false, errores));

        var service = new OrganizadorService(gateway.Object);

        var ex = await Assert.ThrowsAsync<PasswordInvalidaException>(() =>
            service.RegistrarAsync(CrearRequest(password: "abc")));

        Assert.Contains("Se requiere al menos un dígito.", ex.Message);
        Assert.DoesNotContain("abc", ex.Message);
    }

    [Fact]
    public async Task RegistrarAsync_ConTelefonoInvalido_LanzaExcepcionYNoLlamaAlGateway()
    {
        var gateway = new Mock<IIdentityGateway>();
        var service = new OrganizadorService(gateway.Object);

        await Assert.ThrowsAsync<TelefonoInvalidoException>(() =>
            service.RegistrarAsync(CrearRequest(telefono: "123")));

        gateway.Verify(g => g.ExisteMailAsync(It.IsAny<string>()), Times.Never());
        gateway.Verify(
            g => g.CrearUsuarioAsync(It.IsAny<Domain.Organizadores.Organizador>(), It.IsAny<string>()),
            Times.Never());
    }
}
