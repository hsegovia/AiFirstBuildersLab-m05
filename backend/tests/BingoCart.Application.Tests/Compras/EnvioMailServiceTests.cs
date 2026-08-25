using BingoCart.Application.Compras;
using BingoCart.Application.Compras.Dtos;
using BingoCart.Domain.Compras;
using Moq;

namespace BingoCart.Application.Tests.Compras;

/// <summary>
/// Tests unitarios de <see cref="EnvioMailService"/> (spec FEAT-009b, Block 2) — mocks de
/// <see cref="IEnvioMailRepository"/>/<see cref="IEmailSender"/>/<see cref="ICartonPdfRenderer"/>,
/// sin ninguna dependencia real de MailKit/QuestPDF/EF Core (esos son Block 3). El foco de estos
/// tests es la máquina de estados de reintentos (delegada a <see cref="EnvioMail"/>, Block 1) y la
/// mitigación R-04 del threat model: una falla en un envío no aborta el resto del batch.
/// </summary>
public class EnvioMailServiceTests
{
    private static EnvioMailService CrearService(
        Mock<IEnvioMailRepository> envioMailRepository,
        Mock<IEmailSender> emailSender,
        Mock<ICartonPdfRenderer> cartonPdfRenderer,
        TimeProvider? timeProvider = null)
    {
        return new EnvioMailService(
            envioMailRepository.Object,
            emailSender.Object,
            cartonPdfRenderer.Object,
            timeProvider ?? TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<EnvioMailService>.Instance);
    }

    private static Mock<TimeProvider> CrearTimeProviderFijo(DateTime ahoraUtc)
    {
        var timeProvider = new Mock<TimeProvider>();
        timeProvider.Setup(t => t.GetUtcNow()).Returns(new DateTimeOffset(ahoraUtc));
        return timeProvider;
    }

    private static DatosParaMailConfirmacion CrearDatosDeEjemplo(string mailComprador = "comprador@mail.com")
    {
        var cartonId = Guid.NewGuid();
        return new DatosParaMailConfirmacion(
            mailComprador,
            "Juan",
            "Perez",
            new List<CompraParaMail>
            {
                new(
                    Guid.NewGuid(),
                    "Club Uno",
                    300m,
                    new List<CartonParaMail> { new(cartonId, 1, new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }) }),
            });
    }

    [Fact]
    public async Task EncolarConfirmacionAsync_CreaEnvioEnEstadoPendiente()
    {
        var confirmacionId = Guid.NewGuid();
        var compradorId = Guid.NewGuid();

        var envioMailRepository = new Mock<IEnvioMailRepository>();
        var emailSender = new Mock<IEmailSender>();
        var cartonPdfRenderer = new Mock<ICartonPdfRenderer>();

        EnvioMail? envioEncolado = null;
        envioMailRepository
            .Setup(r => r.EncolarAsync(It.IsAny<EnvioMail>()))
            .Callback<EnvioMail>(envio => envioEncolado = envio)
            .Returns(Task.CompletedTask);

        var service = CrearService(envioMailRepository, emailSender, cartonPdfRenderer);

        await service.EncolarConfirmacionAsync(confirmacionId, compradorId);

        Assert.NotNull(envioEncolado);
        Assert.Equal(TipoEnvioMail.Confirmacion, envioEncolado!.TipoEnvio);
        Assert.Equal(confirmacionId, envioEncolado.ConfirmacionId);
        Assert.Equal(compradorId, envioEncolado.CompradorId);
        Assert.Equal(EstadoEnvioMail.Pendiente, envioEncolado.Estado);
        Assert.Equal(0, envioEncolado.Intentos);
        envioMailRepository.Verify(r => r.EncolarAsync(It.IsAny<EnvioMail>()), Times.Once());
    }

    [Fact]
    public async Task EncolarCancelacionAsync_CreaEnvioTipoCancelacionConCompraIdSeteado()
    {
        var compraId = Guid.NewGuid();
        var compradorId = Guid.NewGuid();

        var envioMailRepository = new Mock<IEnvioMailRepository>();
        var emailSender = new Mock<IEmailSender>();
        var cartonPdfRenderer = new Mock<ICartonPdfRenderer>();

        EnvioMail? envioEncolado = null;
        envioMailRepository
            .Setup(r => r.EncolarAsync(It.IsAny<EnvioMail>()))
            .Callback<EnvioMail>(envio => envioEncolado = envio)
            .Returns(Task.CompletedTask);

        var service = CrearService(envioMailRepository, emailSender, cartonPdfRenderer);

        await service.EncolarCancelacionAsync(compraId, compradorId);

        Assert.NotNull(envioEncolado);
        Assert.Equal(TipoEnvioMail.Cancelacion, envioEncolado!.TipoEnvio);
        Assert.Equal(compraId, envioEncolado.CompraId);
        Assert.Null(envioEncolado.ConfirmacionId);
        Assert.Equal(compradorId, envioEncolado.CompradorId);
        Assert.Equal(EstadoEnvioMail.Pendiente, envioEncolado.Estado);
        envioMailRepository.Verify(r => r.EncolarAsync(It.IsAny<EnvioMail>()), Times.Once());
    }

    [Fact]
    public async Task ProcesarPendientesAsync_ConEnvioExitoso_MarcaExitosoYActualiza()
    {
        var ahoraUtc = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var confirmacionId = Guid.NewGuid();
        var envio = EnvioMail.CrearConfirmacion(confirmacionId, Guid.NewGuid(), ahoraUtc.AddMinutes(-5));
        var datos = CrearDatosDeEjemplo();

        var envioMailRepository = new Mock<IEnvioMailRepository>();
        var emailSender = new Mock<IEmailSender>();
        var cartonPdfRenderer = new Mock<ICartonPdfRenderer>();
        var timeProvider = CrearTimeProviderFijo(ahoraUtc);

        envioMailRepository.Setup(r => r.ObtenerPendientesAsync(ahoraUtc)).ReturnsAsync(new List<EnvioMail> { envio });
        envioMailRepository.Setup(r => r.ObtenerDatosParaEnviarAsync(confirmacionId)).ReturnsAsync(datos);
        cartonPdfRenderer
            .Setup(r => r.Renderizar(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<IReadOnlyList<int>>()))
            .Returns(new byte[] { 1, 2, 3 });
        emailSender.Setup(s => s.EnviarAsync(It.IsAny<EnvioMailMensaje>())).Returns(Task.CompletedTask);

        var service = CrearService(envioMailRepository, emailSender, cartonPdfRenderer, timeProvider.Object);

        await service.ProcesarPendientesAsync();

        Assert.Equal(EstadoEnvioMail.Exitoso, envio.Estado);
        envioMailRepository.Verify(r => r.ActualizarAsync(envio), Times.Once());
        emailSender.Verify(
            s => s.EnviarAsync(It.Is<EnvioMailMensaje>(m => m.Destinatario == datos.MailComprador && m.Adjuntos.Count == 1)),
            Times.Once());
    }

    [Fact]
    public async Task ProcesarPendientesAsync_ConFallaAntesDelTercerIntento_RegistraIntentoYSigueEnPendiente()
    {
        var ahoraUtc = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var confirmacionId = Guid.NewGuid();
        var envio = EnvioMail.CrearConfirmacion(confirmacionId, Guid.NewGuid(), ahoraUtc.AddMinutes(-5));
        var datos = CrearDatosDeEjemplo();

        var envioMailRepository = new Mock<IEnvioMailRepository>();
        var emailSender = new Mock<IEmailSender>();
        var cartonPdfRenderer = new Mock<ICartonPdfRenderer>();
        var timeProvider = CrearTimeProviderFijo(ahoraUtc);

        envioMailRepository.Setup(r => r.ObtenerPendientesAsync(ahoraUtc)).ReturnsAsync(new List<EnvioMail> { envio });
        envioMailRepository.Setup(r => r.ObtenerDatosParaEnviarAsync(confirmacionId)).ReturnsAsync(datos);
        emailSender
            .Setup(s => s.EnviarAsync(It.IsAny<EnvioMailMensaje>()))
            .ThrowsAsync(new InvalidOperationException("smtp inalcanzable"));

        var service = CrearService(envioMailRepository, emailSender, cartonPdfRenderer, timeProvider.Object);

        await service.ProcesarPendientesAsync();

        Assert.Equal(1, envio.Intentos);
        Assert.Equal(EstadoEnvioMail.Pendiente, envio.Estado);
        Assert.Equal(ahoraUtc.AddMinutes(1), envio.ProximoIntentoUtc);
        envioMailRepository.Verify(r => r.ActualizarAsync(envio), Times.Once());
    }

    [Fact]
    public async Task ProcesarPendientesAsync_ConFallaEnElTercerIntento_MarcaFallido()
    {
        var ahoraUtc = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var confirmacionId = Guid.NewGuid();
        var envio = EnvioMail.CrearConfirmacion(confirmacionId, Guid.NewGuid(), ahoraUtc.AddMinutes(-10));
        envio.RegistrarIntentoFallido(ahoraUtc.AddMinutes(-8));
        envio.RegistrarIntentoFallido(ahoraUtc.AddMinutes(-4));
        Assert.Equal(2, envio.Intentos);
        Assert.Equal(EstadoEnvioMail.Pendiente, envio.Estado);

        var datos = CrearDatosDeEjemplo();

        var envioMailRepository = new Mock<IEnvioMailRepository>();
        var emailSender = new Mock<IEmailSender>();
        var cartonPdfRenderer = new Mock<ICartonPdfRenderer>();
        var timeProvider = CrearTimeProviderFijo(ahoraUtc);

        envioMailRepository.Setup(r => r.ObtenerPendientesAsync(ahoraUtc)).ReturnsAsync(new List<EnvioMail> { envio });
        envioMailRepository.Setup(r => r.ObtenerDatosParaEnviarAsync(confirmacionId)).ReturnsAsync(datos);
        emailSender
            .Setup(s => s.EnviarAsync(It.IsAny<EnvioMailMensaje>()))
            .ThrowsAsync(new InvalidOperationException("smtp inalcanzable"));

        var service = CrearService(envioMailRepository, emailSender, cartonPdfRenderer, timeProvider.Object);

        await service.ProcesarPendientesAsync();

        Assert.Equal(3, envio.Intentos);
        Assert.Equal(EstadoEnvioMail.Fallido, envio.Estado);
        envioMailRepository.Verify(r => r.ActualizarAsync(envio), Times.Once());
    }

    [Fact]
    public async Task ProcesarPendientesAsync_ConUnEnvioFallandoYOtroExitoso_ProcesaAmbos()
    {
        var ahoraUtc = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var confirmacionIdFallido = Guid.NewGuid();
        var envioFallido = EnvioMail.CrearConfirmacion(confirmacionIdFallido, Guid.NewGuid(), ahoraUtc.AddMinutes(-5));
        var datosFallido = CrearDatosDeEjemplo("fallido@mail.com");

        var confirmacionIdExitoso = Guid.NewGuid();
        var envioExitoso = EnvioMail.CrearConfirmacion(confirmacionIdExitoso, Guid.NewGuid(), ahoraUtc.AddMinutes(-5));
        var datosExitoso = CrearDatosDeEjemplo("exitoso@mail.com");

        var envioMailRepository = new Mock<IEnvioMailRepository>();
        var emailSender = new Mock<IEmailSender>();
        var cartonPdfRenderer = new Mock<ICartonPdfRenderer>();
        var timeProvider = CrearTimeProviderFijo(ahoraUtc);

        envioMailRepository
            .Setup(r => r.ObtenerPendientesAsync(ahoraUtc))
            .ReturnsAsync(new List<EnvioMail> { envioFallido, envioExitoso });
        envioMailRepository.Setup(r => r.ObtenerDatosParaEnviarAsync(confirmacionIdFallido)).ReturnsAsync(datosFallido);
        envioMailRepository.Setup(r => r.ObtenerDatosParaEnviarAsync(confirmacionIdExitoso)).ReturnsAsync(datosExitoso);
        cartonPdfRenderer
            .Setup(r => r.Renderizar(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<IReadOnlyList<int>>()))
            .Returns(new byte[] { 1, 2, 3 });
        emailSender
            .Setup(s => s.EnviarAsync(It.Is<EnvioMailMensaje>(m => m.Destinatario == "fallido@mail.com")))
            .ThrowsAsync(new InvalidOperationException("smtp inalcanzable"));
        emailSender
            .Setup(s => s.EnviarAsync(It.Is<EnvioMailMensaje>(m => m.Destinatario == "exitoso@mail.com")))
            .Returns(Task.CompletedTask);

        var service = CrearService(envioMailRepository, emailSender, cartonPdfRenderer, timeProvider.Object);

        await service.ProcesarPendientesAsync();

        Assert.Equal(EstadoEnvioMail.Pendiente, envioFallido.Estado);
        Assert.Equal(1, envioFallido.Intentos);
        Assert.Equal(EstadoEnvioMail.Exitoso, envioExitoso.Estado);

        envioMailRepository.Verify(r => r.ActualizarAsync(envioFallido), Times.Once());
        envioMailRepository.Verify(r => r.ActualizarAsync(envioExitoso), Times.Once());
    }

    [Fact]
    public async Task ProcesarPendientesAsync_SinDatosParaEnviar_SalteaSinFallar()
    {
        var ahoraUtc = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var confirmacionId = Guid.NewGuid();
        var envio = EnvioMail.CrearConfirmacion(confirmacionId, Guid.NewGuid(), ahoraUtc.AddMinutes(-5));

        var envioMailRepository = new Mock<IEnvioMailRepository>();
        var emailSender = new Mock<IEmailSender>();
        var cartonPdfRenderer = new Mock<ICartonPdfRenderer>();
        var timeProvider = CrearTimeProviderFijo(ahoraUtc);

        envioMailRepository.Setup(r => r.ObtenerPendientesAsync(ahoraUtc)).ReturnsAsync(new List<EnvioMail> { envio });
        envioMailRepository
            .Setup(r => r.ObtenerDatosParaEnviarAsync(confirmacionId))
            .ReturnsAsync((DatosParaMailConfirmacion?)null);

        var service = CrearService(envioMailRepository, emailSender, cartonPdfRenderer, timeProvider.Object);

        await service.ProcesarPendientesAsync();

        Assert.Equal(EstadoEnvioMail.Pendiente, envio.Estado);
        Assert.Equal(0, envio.Intentos);
        emailSender.Verify(s => s.EnviarAsync(It.IsAny<EnvioMailMensaje>()), Times.Never());
        envioMailRepository.Verify(r => r.ActualizarAsync(It.IsAny<EnvioMail>()), Times.Never());
    }

    [Fact]
    public async Task ProcesarPendientesAsync_ConEnvioTipoCancelacion_ArmaMensajeSinAdjuntosYLoEnvia()
    {
        var ahoraUtc = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var compraId = Guid.NewGuid();
        var envio = EnvioMail.CrearCancelacion(compraId, Guid.NewGuid(), ahoraUtc.AddMinutes(-5));
        var datos = new DatosParaMailCancelacion("comprador@mail.com", "Juan", "Perez", compraId, "Club Uno");

        var envioMailRepository = new Mock<IEnvioMailRepository>();
        var emailSender = new Mock<IEmailSender>();
        var cartonPdfRenderer = new Mock<ICartonPdfRenderer>();
        var timeProvider = CrearTimeProviderFijo(ahoraUtc);

        envioMailRepository.Setup(r => r.ObtenerPendientesAsync(ahoraUtc)).ReturnsAsync(new List<EnvioMail> { envio });
        envioMailRepository.Setup(r => r.ObtenerDatosParaCancelacionAsync(compraId)).ReturnsAsync(datos);
        emailSender.Setup(s => s.EnviarAsync(It.IsAny<EnvioMailMensaje>())).Returns(Task.CompletedTask);

        var service = CrearService(envioMailRepository, emailSender, cartonPdfRenderer, timeProvider.Object);

        await service.ProcesarPendientesAsync();

        Assert.Equal(EstadoEnvioMail.Exitoso, envio.Estado);
        envioMailRepository.Verify(r => r.ActualizarAsync(envio), Times.Once());
        emailSender.Verify(
            s => s.EnviarAsync(It.Is<EnvioMailMensaje>(m => m.Destinatario == datos.MailComprador && m.Adjuntos.Count == 0)),
            Times.Once());
        // Un mail de cancelación no adjunta PDFs (no hay cartones que confirmar, se están
        // liberando) — nunca se invoca el renderer.
        cartonPdfRenderer.Verify(r => r.Renderizar(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<IReadOnlyList<int>>()), Times.Never());
    }

    [Fact]
    public async Task ProcesarPendientesAsync_ConEnvioTipoCancelacionYDatosNulos_SalteaSinFallar()
    {
        var ahoraUtc = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var compraId = Guid.NewGuid();
        var envio = EnvioMail.CrearCancelacion(compraId, Guid.NewGuid(), ahoraUtc.AddMinutes(-5));

        var envioMailRepository = new Mock<IEnvioMailRepository>();
        var emailSender = new Mock<IEmailSender>();
        var cartonPdfRenderer = new Mock<ICartonPdfRenderer>();
        var timeProvider = CrearTimeProviderFijo(ahoraUtc);

        envioMailRepository.Setup(r => r.ObtenerPendientesAsync(ahoraUtc)).ReturnsAsync(new List<EnvioMail> { envio });
        envioMailRepository
            .Setup(r => r.ObtenerDatosParaCancelacionAsync(compraId))
            .ReturnsAsync((DatosParaMailCancelacion?)null);

        var service = CrearService(envioMailRepository, emailSender, cartonPdfRenderer, timeProvider.Object);

        await service.ProcesarPendientesAsync();

        Assert.Equal(EstadoEnvioMail.Pendiente, envio.Estado);
        Assert.Equal(0, envio.Intentos);
        emailSender.Verify(s => s.EnviarAsync(It.IsAny<EnvioMailMensaje>()), Times.Never());
        envioMailRepository.Verify(r => r.ActualizarAsync(It.IsAny<EnvioMail>()), Times.Never());
    }

    // Spec FEAT-009d, Block 2 (FR-12)
    [Fact]
    public async Task ProcesarPendientesAsync_ConConfirmacion_PasaElNumeroCorrelativoDelCartonAlRenderizarElPdf()
    {
        var ahoraUtc = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var confirmacionId = Guid.NewGuid();
        var envio = EnvioMail.CrearConfirmacion(confirmacionId, Guid.NewGuid(), ahoraUtc.AddMinutes(-5));
        var cartonId = Guid.NewGuid();
        var numeros = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        var datos = new DatosParaMailConfirmacion(
            "comprador@mail.com",
            "Juan",
            "Perez",
            new List<CompraParaMail>
            {
                new(Guid.NewGuid(), "Club Uno", 100m, new List<CartonParaMail> { new(cartonId, 23, numeros) }),
            });

        var envioMailRepository = new Mock<IEnvioMailRepository>();
        var emailSender = new Mock<IEmailSender>();
        var cartonPdfRenderer = new Mock<ICartonPdfRenderer>();
        var timeProvider = CrearTimeProviderFijo(ahoraUtc);

        envioMailRepository.Setup(r => r.ObtenerPendientesAsync(ahoraUtc)).ReturnsAsync(new List<EnvioMail> { envio });
        envioMailRepository.Setup(r => r.ObtenerDatosParaEnviarAsync(confirmacionId)).ReturnsAsync(datos);
        cartonPdfRenderer
            .Setup(r => r.Renderizar(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<IReadOnlyList<int>>()))
            .Returns(new byte[] { 1, 2, 3 });
        emailSender.Setup(s => s.EnviarAsync(It.IsAny<EnvioMailMensaje>())).Returns(Task.CompletedTask);

        var service = CrearService(envioMailRepository, emailSender, cartonPdfRenderer, timeProvider.Object);

        await service.ProcesarPendientesAsync();

        cartonPdfRenderer.Verify(r => r.Renderizar(cartonId, 23, numeros), Times.Once());
    }

    // Spec FEAT-009d, Block 2 (FR-12/AC-14): el cuerpo del mail identifica cada cartón por su
    // correlativo, el mismo número que el comprador ya vio en el descubrimiento, en el carrito y en
    // la confirmación. El GUID no aparece en el cuerpo: no le dice nada al comprador y era la única
    // superficie del recorrido donde lo veía.
    [Fact]
    public async Task ProcesarPendientesAsync_ConConfirmacion_IdentificaCadaCartonPorSuCorrelativoEnElCuerpoDelMail()
    {
        var ahoraUtc = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var confirmacionId = Guid.NewGuid();
        var envio = EnvioMail.CrearConfirmacion(confirmacionId, Guid.NewGuid(), ahoraUtc.AddMinutes(-5));
        var cartonId = Guid.NewGuid();
        var numeros = new List<int> { 3, 7, 11, 19, 22, 34, 41, 55, 67, 88 };
        var datos = new DatosParaMailConfirmacion(
            "comprador@mail.com",
            "Juan",
            "Perez",
            new List<CompraParaMail>
            {
                new(Guid.NewGuid(), "Club Uno", 100m, new List<CartonParaMail> { new(cartonId, 23, numeros) }),
            });

        var envioMailRepository = new Mock<IEnvioMailRepository>();
        var emailSender = new Mock<IEmailSender>();
        var cartonPdfRenderer = new Mock<ICartonPdfRenderer>();
        var timeProvider = CrearTimeProviderFijo(ahoraUtc);

        envioMailRepository.Setup(r => r.ObtenerPendientesAsync(ahoraUtc)).ReturnsAsync(new List<EnvioMail> { envio });
        envioMailRepository.Setup(r => r.ObtenerDatosParaEnviarAsync(confirmacionId)).ReturnsAsync(datos);
        cartonPdfRenderer
            .Setup(r => r.Renderizar(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<IReadOnlyList<int>>()))
            .Returns(new byte[] { 1, 2, 3 });

        EnvioMailMensaje? mensajeEnviado = null;
        emailSender
            .Setup(s => s.EnviarAsync(It.IsAny<EnvioMailMensaje>()))
            .Callback<EnvioMailMensaje>(mensaje => mensajeEnviado = mensaje)
            .Returns(Task.CompletedTask);

        var service = CrearService(envioMailRepository, emailSender, cartonPdfRenderer, timeProvider.Object);

        await service.ProcesarPendientesAsync();

        Assert.NotNull(mensajeEnviado);
        Assert.Contains("Cartón N° 23", mensajeEnviado!.CuerpoHtml);
        Assert.DoesNotContain(cartonId.ToString(), mensajeEnviado.CuerpoHtml);
        // Los números del cartón, que el cuerpo ya listaba (AC-02 de FEAT-009b), siguen ahí.
        Assert.Contains("3, 7, 11, 19, 22, 34, 41, 55, 67, 88", mensajeEnviado.CuerpoHtml);
    }
}
