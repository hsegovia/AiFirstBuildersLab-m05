using BingoCart.Application.Compras;
using BingoCart.Application.Compras.Dtos;
using BingoCart.Domain.Compras;
using BingoCart.Domain.Compras.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BingoCart.Application.Tests.Compras;

/// <summary>
/// Tests unitarios de <see cref="CompraOrganizadorService"/> (spec FEAT-009c, Block 2) — mocks de
/// <see cref="ICompraRepository"/>/<see cref="IEnvioMailService"/>, sin dependencia real de EF Core.
/// Flujo del organizador (ConfirmarPago/Cancelar/Listar), distinto del flujo del comprador que ya
/// cubre <see cref="CompraServiceTests"/>.
/// </summary>
public class CompraOrganizadorServiceTests
{
    private static CompraOrganizadorService CrearService(
        Mock<ICompraRepository> compraRepository,
        Mock<IEnvioMailService>? envioMailService = null)
    {
        return new CompraOrganizadorService(
            compraRepository.Object,
            (envioMailService ?? new Mock<IEnvioMailService>()).Object,
            NullLogger<CompraOrganizadorService>.Instance);
    }

    private static Compra CompraDePrueba(Guid organizadorId, Guid? compradorId = null)
    {
        var items = new List<ItemCompra> { new(Guid.NewGuid(), 100m) };
        return Compra.Crear(
            organizadorId,
            compradorId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            items,
            MedioPago.Efectivo,
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task ConfirmarPagoAsync_ConCompraPropiaPendiente_LlamaConfirmarPagoYGuardaCambios()
    {
        var organizadorId = Guid.NewGuid();
        var compra = CompraDePrueba(organizadorId);

        var compraRepository = new Mock<ICompraRepository>();
        compraRepository.Setup(r => r.ObtenerPorIdAsync(compra.Id)).ReturnsAsync(compra);
        compraRepository.Setup(r => r.GuardarCambiosAsync()).Returns(Task.CompletedTask);

        var service = CrearService(compraRepository);

        await service.ConfirmarPagoAsync(compra.Id, organizadorId);

        Assert.Equal(EstadoCompra.Confirmado, compra.Estado);
        compraRepository.Verify(r => r.GuardarCambiosAsync(), Times.Once());
    }

    [Fact]
    public async Task ConfirmarPagoAsync_ConCompraAjena_LanzaCompraNoEncontradaException()
    {
        var organizadorId = Guid.NewGuid();
        var otroOrganizadorId = Guid.NewGuid();
        var compra = CompraDePrueba(otroOrganizadorId);

        var compraRepository = new Mock<ICompraRepository>();
        compraRepository.Setup(r => r.ObtenerPorIdAsync(compra.Id)).ReturnsAsync(compra);

        var service = CrearService(compraRepository);

        // Mismo tipo de excepción que "compra inexistente" (no-enumeración, FR-08/AC-08).
        await Assert.ThrowsAsync<CompraNoEncontradaException>(() =>
            service.ConfirmarPagoAsync(compra.Id, organizadorId));

        compraRepository.Verify(r => r.GuardarCambiosAsync(), Times.Never());
    }

    [Fact]
    public async Task ConfirmarPagoAsync_ConCompraInexistente_LanzaCompraNoEncontradaException()
    {
        var organizadorId = Guid.NewGuid();

        var compraRepository = new Mock<ICompraRepository>();
        compraRepository.Setup(r => r.ObtenerPorIdAsync(It.IsAny<Guid>())).ReturnsAsync((Compra?)null);

        var service = CrearService(compraRepository);

        await Assert.ThrowsAsync<CompraNoEncontradaException>(() =>
            service.ConfirmarPagoAsync(Guid.NewGuid(), organizadorId));
    }

    [Fact]
    public async Task ConfirmarPagoAsync_ConCompraYaConfirmada_PropagaCompraEstadoInvalidoException()
    {
        var organizadorId = Guid.NewGuid();
        var compra = CompraDePrueba(organizadorId);
        compra.ConfirmarPago();

        var compraRepository = new Mock<ICompraRepository>();
        compraRepository.Setup(r => r.ObtenerPorIdAsync(compra.Id)).ReturnsAsync(compra);

        var service = CrearService(compraRepository);

        await Assert.ThrowsAsync<CompraEstadoInvalidoException>(() =>
            service.ConfirmarPagoAsync(compra.Id, organizadorId));

        compraRepository.Verify(r => r.GuardarCambiosAsync(), Times.Never());
    }

    [Fact]
    public async Task CancelarAsync_ConCompraPropiaPendiente_TransicionaYEncolaCancelacion()
    {
        var organizadorId = Guid.NewGuid();
        var compra = CompraDePrueba(organizadorId);

        var compraRepository = new Mock<ICompraRepository>(MockBehavior.Strict);
        var envioMailService = new Mock<IEnvioMailService>(MockBehavior.Strict);

        var secuencia = new MockSequence();

        compraRepository.Setup(r => r.ObtenerPorIdAsync(compra.Id)).ReturnsAsync(compra);
        compraRepository
            .InSequence(secuencia)
            .Setup(r => r.GuardarCambiosAsync())
            .Returns(Task.CompletedTask);
        envioMailService
            .InSequence(secuencia)
            .Setup(s => s.EncolarCancelacionAsync(compra.Id, compra.CompradorId))
            .Returns(Task.CompletedTask);

        var service = CrearService(compraRepository, envioMailService);

        await service.CancelarAsync(compra.Id, organizadorId);

        // MockSequence (Moq) rechaza la llamada si no respeta el orden configurado — si
        // EncolarCancelacionAsync se invocara ANTES de GuardarCambiosAsync, el setup no matchearía
        // y Moq lanzaría MockException al invocar el método (ambos mocks son Strict), por lo que
        // llegar hasta acá sin excepción ya prueba el orden. Valida FR-03/FR-06.
        Assert.Equal(EstadoCompra.Cancelado, compra.Estado);
        compraRepository.Verify(r => r.GuardarCambiosAsync(), Times.Once());
        envioMailService.Verify(s => s.EncolarCancelacionAsync(compra.Id, compra.CompradorId), Times.Once());
    }

    [Fact]
    public async Task CancelarAsync_ConEncolarCancelacionLanzandoExcepcion_LaCancelacionIgualQuedaPersistida()
    {
        var organizadorId = Guid.NewGuid();
        var compra = CompraDePrueba(organizadorId);

        var compraRepository = new Mock<ICompraRepository>();
        var envioMailService = new Mock<IEnvioMailService>();

        compraRepository.Setup(r => r.ObtenerPorIdAsync(compra.Id)).ReturnsAsync(compra);
        compraRepository.Setup(r => r.GuardarCambiosAsync()).Returns(Task.CompletedTask);
        envioMailService
            .Setup(s => s.EncolarCancelacionAsync(compra.Id, compra.CompradorId))
            .ThrowsAsync(new InvalidOperationException("outbox no disponible"));

        var service = CrearService(compraRepository, envioMailService);

        // FR-06 (best-effort): la falla al encolar el mail de cancelación nunca se propaga — la
        // cancelación ya persistida en SQL no se revierte ni la excepción llega al llamador.
        await service.CancelarAsync(compra.Id, organizadorId);

        Assert.Equal(EstadoCompra.Cancelado, compra.Estado);
        compraRepository.Verify(r => r.GuardarCambiosAsync(), Times.Once());
    }

    [Fact]
    public async Task CancelarAsync_ConCompraAjena_LanzaCompraNoEncontradaException()
    {
        var organizadorId = Guid.NewGuid();
        var otroOrganizadorId = Guid.NewGuid();
        var compra = CompraDePrueba(otroOrganizadorId);

        var compraRepository = new Mock<ICompraRepository>();
        compraRepository.Setup(r => r.ObtenerPorIdAsync(compra.Id)).ReturnsAsync(compra);

        var service = CrearService(compraRepository);

        await Assert.ThrowsAsync<CompraNoEncontradaException>(() =>
            service.CancelarAsync(compra.Id, organizadorId));

        compraRepository.Verify(r => r.GuardarCambiosAsync(), Times.Never());
    }

    [Fact]
    public async Task CancelarAsync_ConCompraYaCancelada_PropagaCompraEstadoInvalidoException()
    {
        var organizadorId = Guid.NewGuid();
        var compra = CompraDePrueba(organizadorId);
        compra.Cancelar();

        var compraRepository = new Mock<ICompraRepository>();
        compraRepository.Setup(r => r.ObtenerPorIdAsync(compra.Id)).ReturnsAsync(compra);

        var service = CrearService(compraRepository);

        await Assert.ThrowsAsync<CompraEstadoInvalidoException>(() =>
            service.CancelarAsync(compra.Id, organizadorId));

        compraRepository.Verify(r => r.GuardarCambiosAsync(), Times.Never());
    }

    [Fact]
    public async Task ListarPropiasAsync_ConPageSizeMayorA50_ClampeaA50()
    {
        var organizadorId = Guid.NewGuid();

        var compraRepository = new Mock<ICompraRepository>();
        compraRepository
            .Setup(r => r.ListarPorOrganizadorAsync(organizadorId, 1, 50))
            .ReturnsAsync(new ComprasPaginadas(Array.Empty<CompraConMonto>(), 0));

        var service = CrearService(compraRepository);

        var response = await service.ListarPropiasAsync(organizadorId, 1, 200);

        // NFR-01: 50 es el máximo propio de este servicio, independiente del clamp de 100 que usa
        // BingoService.ListarPropiosAsync.
        Assert.Equal(50, response.PageSize);
        compraRepository.Verify(r => r.ListarPorOrganizadorAsync(organizadorId, 1, 50), Times.Once());
    }

    [Fact]
    public async Task ListarPropiasAsync_DevuelveCompraResumenSinDatosDelComprador()
    {
        var organizadorId = Guid.NewGuid();
        var compra = CompraDePrueba(organizadorId);

        var compraRepository = new Mock<ICompraRepository>();
        compraRepository
            .Setup(r => r.ListarPorOrganizadorAsync(organizadorId, 1, 20))
            .ReturnsAsync(new ComprasPaginadas(new List<CompraConMonto> { new(compra, 150m) }, 1));

        var service = CrearService(compraRepository);

        var response = await service.ListarPropiasAsync(organizadorId, 1, 20);

        Assert.Single(response.Items);
        var item = response.Items[0];
        Assert.Equal(compra.Id, item.CompraId);
        Assert.Equal(compra.Estado.ToString(), item.Estado);
        Assert.Equal(150m, item.MontoTotal);
        Assert.Equal(compra.FechaCreacionUtc, item.FechaCreacionUtc);

        // FR-07/AC-07: CompraResumenResponse solo tiene estos 4 campos — ningún dato del comprador.
        var propiedades = typeof(CompraResumenResponse).GetProperties().Select(p => p.Name).ToList();
        Assert.Equal(new[] { "CompraId", "Estado", "MontoTotal", "FechaCreacionUtc" }, propiedades);
    }
}
