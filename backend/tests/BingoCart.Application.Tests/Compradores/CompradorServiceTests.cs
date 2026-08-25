using BingoCart.Application.Auth;
using BingoCart.Application.Compradores;
using BingoCart.Application.Compradores.Dtos;
using BingoCart.Application.Organizadores;
using BingoCart.Domain.Auth.Exceptions;
using BingoCart.Domain.Compradores.Exceptions;
using Microsoft.Extensions.Logging;
using Moq;

namespace BingoCart.Application.Tests.Compradores;

/// <summary>
/// Tests unitarios de <see cref="CompradorService"/> (spec FEAT-009a Block 2, spec FEAT-009d
/// Block 5) — mock de <see cref="ICompradorIdentityGateway"/>/<see cref="ICompradorCuentaRepository"/>,
/// mismo patrón que <c>OrganizadorServiceTests</c>. El constructor pasó a recibir además
/// <see cref="ICompradorCuentaRepository"/> (consulta de sorteo inminente, D-03: puerto propio),
/// <see cref="TimeProvider"/> (NFR-05: cero <c>DateTime.UtcNow</c> en código productivo) y
/// <see cref="ILogger{TCategoryName}"/> (log de auditoría sin PII, mitigación de R-05) — necesarios
/// para el bloque de "datos de cuenta" y sin los cuales <c>ActualizarCuentaAsync</c> no podría
/// aplicar la regla de los 60 minutos ni auditar el cambio, aunque el texto del spec (Files, Block 5)
/// solo mencione la incorporación de <c>TimeProvider</c> — ver el reporte del bloque.
/// </summary>
public class CompradorServiceTests
{
    // Mismo CUIT válido conocido usado en BingoCart.Domain.Tests.Compradores.CompradorTests.
    private const string CuitValido = "30500010912";

    // Segundo CUIT válido conocido (CuitValidatorTests: resto 0 -> dígito esperado 0), usado en los
    // tests de colisión para no reutilizar el mismo CUIT que la "propia cuenta".
    private const string OtroCuitValido = "00000000000";

    private const string PasswordValida = "Passw0rd!";

    private static readonly Guid CompradorId = Guid.NewGuid();

    private static RegistrarCompradorRequest CrearRequest(
        string apellido = "Pérez",
        string nombre = "Juan",
        string cuit = CuitValido,
        string mail = "juan.perez@example.com",
        string password = PasswordValida)
    {
        return new RegistrarCompradorRequest(apellido, nombre, cuit, mail, password);
    }

    private static ActualizarCuentaRequest CrearActualizarRequest(
        string apellido = "Gómez",
        string nombre = "María",
        string cuit = CuitValido,
        string mail = "nuevo.mail@example.com",
        string contrasenaActual = "ClaveActual1!")
    {
        return new ActualizarCuentaRequest(apellido, nombre, cuit, mail, contrasenaActual);
    }

    private static CompradorService CrearService(
        Mock<ICompradorIdentityGateway> gateway,
        Mock<ICompradorCuentaRepository>? cuentaRepository = null,
        Mock<IJwtTokenService>? jwtTokenService = null,
        TimeProvider? timeProvider = null,
        ILogger<CompradorService>? logger = null)
    {
        return new CompradorService(
            gateway.Object,
            (cuentaRepository ?? new Mock<ICompradorCuentaRepository>()).Object,
            (jwtTokenService ?? new Mock<IJwtTokenService>()).Object,
            timeProvider ?? TimeProvider.System,
            logger ?? new CapturingLogger<CompradorService>());
    }

    /// <summary>
    /// Configura el gateway/repositorio mockeados para que <c>ActualizarCuentaAsync</c> llegue hasta
    /// la escritura sin ningún rechazo — punto de partida de los tests de camino feliz y de los sad
    /// paths, que sobreescriben solo el mock puntual que necesitan hacer fallar.
    /// </summary>
    private static void ConfigurarCaminoFeliz(
        Mock<ICompradorIdentityGateway> gateway,
        Mock<ICompradorCuentaRepository> cuentaRepository,
        CuentaCompradorResponse? cuentaActual = null)
    {
        gateway.Setup(g => g.VerificarPasswordAsync(CompradorId, It.IsAny<string>())).ReturnsAsync(true);
        gateway.Setup(g => g.ExisteMailDeOtraCuentaAsync(CompradorId, It.IsAny<string>())).ReturnsAsync(false);
        gateway.Setup(g => g.ExisteCuitDeOtraCuentaAsync(CompradorId, It.IsAny<string>())).ReturnsAsync(false);
        gateway.Setup(g => g.ObtenerCuentaAsync(CompradorId))
            .ReturnsAsync(cuentaActual
                ?? new CuentaCompradorResponse("ApellidoViejo", "NombreViejo", CuitValido, "viejo.mail@example.com"));
        gateway
            .Setup(g => g.ActualizarDatosAsync(
                CompradorId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new IdentityGatewayResult(true, Array.Empty<string>()));
        cuentaRepository
            .Setup(r => r.TieneSorteoInminenteAsync(CompradorId, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(false);
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

        var service = CrearService(gateway, jwtTokenService: jwtTokenService);

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

    // ---------------------------------------------------------------------------------------
    // ActualizarCuentaAsync (spec FEAT-009d, Block 5)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task ActualizarCuentaAsync_ConTodosLosSorteosLejanos_PersisteLosCambios()
    {
        var gateway = new Mock<ICompradorIdentityGateway>();
        var cuentaRepository = new Mock<ICompradorCuentaRepository>();
        ConfigurarCaminoFeliz(gateway, cuentaRepository);

        var service = CrearService(gateway, cuentaRepository);

        var request = CrearActualizarRequest();
        var response = await service.ActualizarCuentaAsync(CompradorId, request);

        Assert.Equal(request.Apellido, response.Apellido);
        Assert.Equal(request.Nombre, response.Nombre);
        Assert.Equal(request.Cuit, response.Cuit);
        Assert.Equal(request.Mail, response.Mail);
        gateway.Verify(
            g => g.ActualizarDatosAsync(CompradorId, request.Apellido, request.Nombre, request.Cuit, request.Mail),
            Times.Once());
    }

    [Fact]
    public async Task ActualizarCuentaAsync_ConSorteoDentroDe60Minutos_LanzaPlazoModificacionVencidoException()
    {
        var gateway = new Mock<ICompradorIdentityGateway>();
        var cuentaRepository = new Mock<ICompradorCuentaRepository>();
        ConfigurarCaminoFeliz(gateway, cuentaRepository);
        cuentaRepository
            .Setup(r => r.TieneSorteoInminenteAsync(CompradorId, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(true);

        var service = CrearService(gateway, cuentaRepository);

        await Assert.ThrowsAsync<PlazoModificacionVencidoException>(() =>
            service.ActualizarCuentaAsync(CompradorId, CrearActualizarRequest()));

        gateway.Verify(
            g => g.ActualizarDatosAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never());
    }

    [Fact]
    public async Task ActualizarCuentaAsync_ConSorteoYaPasado_PermiteLaActualizacion()
    {
        // D-01: un sorteo ya pasado NO debe bloquear — la lectura literal de la ventana congelaría
        // la cuenta para siempre tras la primera compra. A nivel de este mock, el repositorio ya
        // filtró los sorteos pasados y devuelve `false` — el service simplemente confía en ese
        // resultado y avanza (la lógica de filtrado real se prueba en CompradorCuentaRepositoryTests).
        var gateway = new Mock<ICompradorIdentityGateway>();
        var cuentaRepository = new Mock<ICompradorCuentaRepository>();
        ConfigurarCaminoFeliz(gateway, cuentaRepository);

        var service = CrearService(gateway, cuentaRepository);

        var response = await service.ActualizarCuentaAsync(CompradorId, CrearActualizarRequest());

        Assert.NotNull(response);
        gateway.Verify(
            g => g.ActualizarDatosAsync(
                CompradorId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Once());
    }

    [Fact]
    public async Task ActualizarCuentaAsync_ConCompraCanceladaYSorteoInminente_PermiteLaActualizacion()
    {
        // D-02: una compra Cancelado ya no tiene cartones vigentes, así que ni siquiera con un sorteo
        // dentro de la ventana debe bloquear — de nuevo, el mock representa el resultado ya filtrado
        // del repositorio (`false`), cuya lógica real de exclusión se prueba en
        // CompradorCuentaRepositoryTests.TieneSorteoInminenteAsync_IgnoraComprasCanceladasYSorteosPasados.
        var gateway = new Mock<ICompradorIdentityGateway>();
        var cuentaRepository = new Mock<ICompradorCuentaRepository>();
        ConfigurarCaminoFeliz(gateway, cuentaRepository);

        var service = CrearService(gateway, cuentaRepository);

        var response = await service.ActualizarCuentaAsync(CompradorId, CrearActualizarRequest());

        Assert.NotNull(response);
    }

    [Fact]
    public async Task ActualizarCuentaAsync_ConMailDeOtraCuenta_LanzaMailEnUsoExceptionSinModificarNada()
    {
        var gateway = new Mock<ICompradorIdentityGateway>();
        var cuentaRepository = new Mock<ICompradorCuentaRepository>();
        ConfigurarCaminoFeliz(gateway, cuentaRepository);
        gateway.Setup(g => g.ExisteMailDeOtraCuentaAsync(CompradorId, It.IsAny<string>())).ReturnsAsync(true);

        var service = CrearService(gateway, cuentaRepository);

        await Assert.ThrowsAsync<MailEnUsoException>(() =>
            service.ActualizarCuentaAsync(CompradorId, CrearActualizarRequest()));

        gateway.Verify(
            g => g.ActualizarDatosAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never());
    }

    [Fact]
    public async Task ActualizarCuentaAsync_ConCuitDeOtraCuenta_LanzaCuitEnUsoExceptionSinModificarNada()
    {
        var gateway = new Mock<ICompradorIdentityGateway>();
        var cuentaRepository = new Mock<ICompradorCuentaRepository>();
        ConfigurarCaminoFeliz(gateway, cuentaRepository);
        gateway.Setup(g => g.ExisteCuitDeOtraCuentaAsync(CompradorId, It.IsAny<string>())).ReturnsAsync(true);

        var service = CrearService(gateway, cuentaRepository);

        await Assert.ThrowsAsync<CuitEnUsoException>(() =>
            service.ActualizarCuentaAsync(CompradorId, CrearActualizarRequest(cuit: OtroCuitValido)));

        gateway.Verify(
            g => g.ActualizarDatosAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never());
    }

    [Fact]
    public async Task ActualizarCuentaAsync_ConCuitDeLongitudInvalida_LanzaCuitInvalidoExceptionIndicandoLaLongitud()
    {
        var gateway = new Mock<ICompradorIdentityGateway>();
        var cuentaRepository = new Mock<ICompradorCuentaRepository>();
        ConfigurarCaminoFeliz(gateway, cuentaRepository);

        var service = CrearService(gateway, cuentaRepository);

        var ex = await Assert.ThrowsAsync<CuitInvalidoException>(() =>
            service.ActualizarCuentaAsync(CompradorId, CrearActualizarRequest(cuit: "12345")));

        Assert.Contains("11 dígitos", ex.Message);
        gateway.Verify(g => g.VerificarPasswordAsync(CompradorId, It.IsAny<string>()), Times.Once());
        gateway.Verify(g => g.ExisteMailDeOtraCuentaAsync(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never());
    }

    [Fact]
    public async Task ActualizarCuentaAsync_ConDigitoVerificadorInvalido_LanzaCuitInvalidoExceptionIndicandoElDigito()
    {
        var gateway = new Mock<ICompradorIdentityGateway>();
        var cuentaRepository = new Mock<ICompradorCuentaRepository>();
        ConfigurarCaminoFeliz(gateway, cuentaRepository);

        var service = CrearService(gateway, cuentaRepository);

        // Mismo CUIT de 11 dígitos numéricos que CuitValido, con el último dígito alterado —
        // conocido inválido por dígito verificador (BingoCart.Domain.Tests.Organizadores.CuitValidatorTests).
        var ex = await Assert.ThrowsAsync<CuitInvalidoException>(() =>
            service.ActualizarCuentaAsync(CompradorId, CrearActualizarRequest(cuit: "30500010910")));

        Assert.Contains("dígito verificador", ex.Message);
    }

    [Fact]
    public async Task ActualizarCuentaAsync_ConElMismoCuitDeLaPropiaCuenta_NoLoTrataComoColision()
    {
        // El gateway (ExisteCuitDeOtraCuentaAsync) es quien excluye a la propia cuenta: cuando el
        // CUIT enviado ya es el que la cuenta tiene, la implementación real devuelve `false` (no hay
        // OTRA cuenta con ese CUIT). Este test documenta que el service, al recibir ese `false`, NO
        // lo trata como colisión y deja avanzar la actualización.
        var gateway = new Mock<ICompradorIdentityGateway>();
        var cuentaRepository = new Mock<ICompradorCuentaRepository>();
        ConfigurarCaminoFeliz(gateway, cuentaRepository,
            cuentaActual: new CuentaCompradorResponse("ApellidoViejo", "NombreViejo", CuitValido, "viejo.mail@example.com"));

        var service = CrearService(gateway, cuentaRepository);

        var response = await service.ActualizarCuentaAsync(
            CompradorId, CrearActualizarRequest(cuit: CuitValido));

        Assert.Equal(CuitValido, response.Cuit);
        gateway.Verify(
            g => g.ActualizarDatosAsync(
                CompradorId, It.IsAny<string>(), It.IsAny<string>(), CuitValido, It.IsAny<string>()),
            Times.Once());
    }

    [Fact]
    public async Task ActualizarCuentaAsync_ConIdentityResultFallido_LanzaExcepcion()
    {
        var gateway = new Mock<ICompradorIdentityGateway>();
        var cuentaRepository = new Mock<ICompradorCuentaRepository>();
        ConfigurarCaminoFeliz(gateway, cuentaRepository);
        gateway
            .Setup(g => g.ActualizarDatosAsync(
                CompradorId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new IdentityGatewayResult(false, new List<string> { "Error interno de Identity." }));

        var service = CrearService(gateway, cuentaRepository);

        // Un IdentityResult no exitoso se traduce a excepción, no se ignora (spec: "un
        // IdentityResult descartado sería un cambio que el usuario cree hecho y no está") — el
        // spec no fija un tipo concreto para este caso, así que se verifica genéricamente que la
        // operación efectivamente lanza en vez de devolver un resultado silencioso.
        var excepcion = await Record.ExceptionAsync(() =>
            service.ActualizarCuentaAsync(CompradorId, CrearActualizarRequest()));

        Assert.NotNull(excepcion);
    }

    [Fact]
    public async Task ActualizarCuentaAsync_ConContrasenaIncorrecta_LanzaContrasenaIncorrectaExceptionSinModificarNada()
    {
        var gateway = new Mock<ICompradorIdentityGateway>();
        var cuentaRepository = new Mock<ICompradorCuentaRepository>();
        ConfigurarCaminoFeliz(gateway, cuentaRepository);
        gateway.Setup(g => g.VerificarPasswordAsync(CompradorId, It.IsAny<string>())).ReturnsAsync(false);

        var service = CrearService(gateway, cuentaRepository);

        await Assert.ThrowsAsync<ContrasenaIncorrectaException>(() =>
            service.ActualizarCuentaAsync(CompradorId, CrearActualizarRequest()));

        // Ninguna otra validación se ejecuta (R-04): ni colisiones, ni el plazo de 60 minutos, ni la
        // escritura.
        gateway.Verify(g => g.ExisteMailDeOtraCuentaAsync(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never());
        gateway.Verify(g => g.ExisteCuitDeOtraCuentaAsync(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never());
        cuentaRepository.Verify(
            r => r.TieneSorteoInminenteAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()),
            Times.Never());
        gateway.Verify(
            g => g.ActualizarDatosAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never());
    }

    [Fact]
    public async Task ActualizarCuentaAsync_ConContrasenaIncorrectaYMailYaEnUso_LanzaContrasenaIncorrectaException()
    {
        // El orden importa (mitigación de R-04): aunque el mail YA esté en uso, con la contraseña
        // mal la respuesta nunca llega a revelarlo — se lanza ContrasenaIncorrectaException, no
        // MailEnUsoException, y ExisteMailDeOtraCuentaAsync ni se invoca.
        var gateway = new Mock<ICompradorIdentityGateway>();
        var cuentaRepository = new Mock<ICompradorCuentaRepository>();
        ConfigurarCaminoFeliz(gateway, cuentaRepository);
        gateway.Setup(g => g.VerificarPasswordAsync(CompradorId, It.IsAny<string>())).ReturnsAsync(false);
        gateway.Setup(g => g.ExisteMailDeOtraCuentaAsync(CompradorId, It.IsAny<string>())).ReturnsAsync(true);

        var service = CrearService(gateway, cuentaRepository);

        await Assert.ThrowsAsync<ContrasenaIncorrectaException>(() =>
            service.ActualizarCuentaAsync(CompradorId, CrearActualizarRequest()));

        gateway.Verify(g => g.ExisteMailDeOtraCuentaAsync(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never());
    }

    [Fact]
    public async Task ActualizarCuentaAsync_ConMailEnUso_NoRepiteLaDireccionEnElMensaje()
    {
        var gateway = new Mock<ICompradorIdentityGateway>();
        var cuentaRepository = new Mock<ICompradorCuentaRepository>();
        ConfigurarCaminoFeliz(gateway, cuentaRepository);
        gateway.Setup(g => g.ExisteMailDeOtraCuentaAsync(CompradorId, It.IsAny<string>())).ReturnsAsync(true);

        var service = CrearService(gateway, cuentaRepository);
        var request = CrearActualizarRequest(mail: "mail.filtrado@example.com");

        var ex = await Assert.ThrowsAsync<MailEnUsoException>(() =>
            service.ActualizarCuentaAsync(CompradorId, request));

        Assert.DoesNotContain("mail.filtrado@example.com", ex.Message);
    }

    [Fact]
    public async Task ActualizarCuentaAsync_ConCuitEnUso_NoRepiteElCuitEnElMensaje()
    {
        var gateway = new Mock<ICompradorIdentityGateway>();
        var cuentaRepository = new Mock<ICompradorCuentaRepository>();
        ConfigurarCaminoFeliz(gateway, cuentaRepository);
        gateway.Setup(g => g.ExisteCuitDeOtraCuentaAsync(CompradorId, It.IsAny<string>())).ReturnsAsync(true);

        var service = CrearService(gateway, cuentaRepository);
        var request = CrearActualizarRequest(cuit: OtroCuitValido);

        var ex = await Assert.ThrowsAsync<CuitEnUsoException>(() =>
            service.ActualizarCuentaAsync(CompradorId, request));

        Assert.DoesNotContain(OtroCuitValido, ex.Message);
    }

    [Fact]
    public async Task ActualizarCuentaAsync_Exitosa_RegistraAuditoriaConNombresDeCamposYSinValores()
    {
        var gateway = new Mock<ICompradorIdentityGateway>();
        var cuentaRepository = new Mock<ICompradorCuentaRepository>();
        var cuentaActual = new CuentaCompradorResponse(
            "ApellidoViejo", "NombreViejo", "30500010912", "mail.viejo.secreto@example.com");
        ConfigurarCaminoFeliz(gateway, cuentaRepository, cuentaActual);

        var logger = new CapturingLogger<CompradorService>();
        var service = CrearService(gateway, cuentaRepository, logger: logger);

        var request = CrearActualizarRequest(
            apellido: "ApellidoNuevoSecreto",
            nombre: "NombreNuevoSecreto",
            cuit: OtroCuitValido,
            mail: "mail.nuevo.secreto@example.com",
            contrasenaActual: "LaClaveSecreta1!");

        await service.ActualizarCuentaAsync(CompradorId, request);

        Assert.Single(logger.MensajesCapturados);
        var mensaje = logger.MensajesCapturados.Single();

        // Sí: compradorId y los NOMBRES de los cuatro campos que cambiaron.
        Assert.Contains(CompradorId.ToString(), mensaje);
        Assert.Contains(nameof(ActualizarCuentaRequest.Apellido), mensaje);
        Assert.Contains(nameof(ActualizarCuentaRequest.Nombre), mensaje);
        Assert.Contains(nameof(ActualizarCuentaRequest.Cuit), mensaje);
        Assert.Contains(nameof(ActualizarCuentaRequest.Mail), mensaje);

        // No: ningún valor de PII (ni el anterior ni el nuevo) ni la contraseña.
        Assert.DoesNotContain("ApellidoViejo", mensaje);
        Assert.DoesNotContain("NombreViejo", mensaje);
        Assert.DoesNotContain("mail.viejo.secreto@example.com", mensaje);
        Assert.DoesNotContain("ApellidoNuevoSecreto", mensaje);
        Assert.DoesNotContain("NombreNuevoSecreto", mensaje);
        Assert.DoesNotContain("mail.nuevo.secreto@example.com", mensaje);
        Assert.DoesNotContain(OtroCuitValido, mensaje);
        Assert.DoesNotContain("30500010912", mensaje);
        Assert.DoesNotContain("LaClaveSecreta1!", mensaje);
    }
}
