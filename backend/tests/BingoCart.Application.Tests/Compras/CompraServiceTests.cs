using BingoCart.Application.Bingos;
using BingoCart.Application.Carritos;
using BingoCart.Application.Carritos.Dtos;
using BingoCart.Application.Compras;
using BingoCart.Application.Compras.Dtos;
using BingoCart.Domain.Carritos;
using BingoCart.Domain.Compras;
using BingoCart.Domain.Compras.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BingoCart.Application.Tests.Compras;

/// <summary>
/// Tests unitarios de <see cref="CompraService"/> (spec FEAT-009a, Block 2) — mocks de
/// <see cref="ICarritoRepository"/>/<see cref="IBingoRepository"/>/<see cref="ICompraRepository"/>.
/// Los tests de orden (AC-09) son los más importantes de este bloque: prueban que
/// <c>LiberarCarritoConfirmadoAsync</c> NUNCA se invoca sin un <c>CrearVariasAsync</c> exitoso
/// previo (R-02 del threat model).
/// </summary>
public class CompraServiceTests
{
    private static CompraService CrearService(
        Mock<ICarritoRepository> carritoRepository,
        Mock<IBingoRepository> bingoRepository,
        Mock<ICompraRepository> compraRepository,
        TimeProvider? timeProvider = null)
    {
        return new CompraService(
            carritoRepository.Object,
            bingoRepository.Object,
            compraRepository.Object,
            timeProvider ?? TimeProvider.System,
            NullLogger<CompraService>.Instance);
    }

    [Fact]
    public async Task ConfirmarCompraAsync_ConCarritoVacio_LanzaCarritoVacioExceptionSinInvocarRevalidarReservasAsync()
    {
        var sesionId = Guid.NewGuid().ToString();
        var compradorId = Guid.NewGuid();

        var carritoRepository = new Mock<ICarritoRepository>();
        var bingoRepository = new Mock<IBingoRepository>();
        var compraRepository = new Mock<ICompraRepository>();

        carritoRepository.Setup(r => r.ObtenerItemsAsync(sesionId)).ReturnsAsync(Array.Empty<ItemCarrito>());

        var service = CrearService(carritoRepository, bingoRepository, compraRepository);

        await Assert.ThrowsAsync<CarritoVacioException>(() =>
            service.ConfirmarCompraAsync(sesionId, compradorId, MedioPago.Efectivo));

        carritoRepository.Verify(r => r.RevalidarReservasAsync(It.IsAny<string>()), Times.Never());
    }

    [Fact]
    public async Task ConfirmarCompraAsync_ConRevalidacionInvalida_LanzaReservaCarritoInvalidaExceptionConLosCartonIdsCorrectosSinInvocarCrearVariasAsync()
    {
        var sesionId = Guid.NewGuid().ToString();
        var compradorId = Guid.NewGuid();
        var cartonIdInvalido = Guid.NewGuid();

        var carritoRepository = new Mock<ICarritoRepository>();
        var bingoRepository = new Mock<IBingoRepository>();
        var compraRepository = new Mock<ICompraRepository>();

        carritoRepository
            .Setup(r => r.ObtenerItemsAsync(sesionId))
            .ReturnsAsync(new List<ItemCarrito> { new(cartonIdInvalido, 100m) });
        carritoRepository
            .Setup(r => r.RevalidarReservasAsync(sesionId))
            .ReturnsAsync(new RevalidacionCarrito(false, Array.Empty<ItemCarrito>(), new[] { cartonIdInvalido }));

        var service = CrearService(carritoRepository, bingoRepository, compraRepository);

        var ex = await Assert.ThrowsAsync<ReservaCarritoInvalidaException>(() =>
            service.ConfirmarCompraAsync(sesionId, compradorId, MedioPago.Efectivo));

        Assert.Single(ex.CartonIdsInvalidos);
        Assert.Equal(cartonIdInvalido, ex.CartonIdsInvalidos[0]);
        compraRepository.Verify(r => r.CrearVariasAsync(It.IsAny<IReadOnlyList<Domain.Compras.Compra>>()), Times.Never());
    }

    [Fact]
    public async Task ConfirmarCompraAsync_Con3CartonesDe2Organizadores_InvocaCrearVariasAsyncConExactamente2Compras()
    {
        var sesionId = Guid.NewGuid().ToString();
        var compradorId = Guid.NewGuid();
        var organizadorUno = Guid.NewGuid();
        var organizadorDos = Guid.NewGuid();

        var cartonUno = Guid.NewGuid();
        var cartonDos = Guid.NewGuid();
        var cartonTres = Guid.NewGuid();

        var items = new List<ItemCarrito>
        {
            new(cartonUno, 100m),
            new(cartonDos, 200m),
            new(cartonTres, 300m),
        };

        var carritoRepository = new Mock<ICarritoRepository>();
        var bingoRepository = new Mock<IBingoRepository>();
        var compraRepository = new Mock<ICompraRepository>();

        carritoRepository.Setup(r => r.ObtenerItemsAsync(sesionId)).ReturnsAsync(items);
        carritoRepository
            .Setup(r => r.RevalidarReservasAsync(sesionId))
            .ReturnsAsync(new RevalidacionCarrito(true, items, Array.Empty<Guid>()));
        bingoRepository
            .Setup(r => r.ObtenerParaConfirmarCompraAsync(It.IsAny<IReadOnlyCollection<Guid>>()))
            .ReturnsAsync(new List<CartonParaConfirmarCompra>
            {
                new(cartonUno, Guid.NewGuid(), organizadorUno, "Club Uno", "Bingo Uno"),
                new(cartonDos, Guid.NewGuid(), organizadorUno, "Club Uno", "Bingo Uno"),
                new(cartonTres, Guid.NewGuid(), organizadorDos, "Club Dos", "Bingo Dos"),
            });

        List<Domain.Compras.Compra>? comprasRecibidas = null;
        compraRepository
            .Setup(r => r.CrearVariasAsync(It.IsAny<IReadOnlyList<Domain.Compras.Compra>>()))
            .Callback<IReadOnlyList<Domain.Compras.Compra>>(compras => comprasRecibidas = compras.ToList())
            .Returns(Task.CompletedTask);

        var service = CrearService(carritoRepository, bingoRepository, compraRepository);

        var response = await service.ConfirmarCompraAsync(sesionId, compradorId, MedioPago.Transferencia);

        Assert.NotNull(comprasRecibidas);
        Assert.Equal(2, comprasRecibidas!.Count);
        Assert.Equal(2, response.Compras.Count);

        var compraOrganizadorUno = comprasRecibidas.Single(c => c.OrganizadorId == organizadorUno);
        Assert.Equal(2, compraOrganizadorUno.Items.Count);
        Assert.Contains(compraOrganizadorUno.Items, i => i.CartonId == cartonUno);
        Assert.Contains(compraOrganizadorUno.Items, i => i.CartonId == cartonDos);

        var compraOrganizadorDos = comprasRecibidas.Single(c => c.OrganizadorId == organizadorDos);
        Assert.Single(compraOrganizadorDos.Items);
        Assert.Equal(cartonTres, compraOrganizadorDos.Items[0].CartonId);
    }

    [Fact]
    public async Task ConfirmarCompraAsync_Exitoso_InvocaLiberarCarritoConfirmadoAsyncDespuesDeCrearVariasAsync()
    {
        var sesionId = Guid.NewGuid().ToString();
        var compradorId = Guid.NewGuid();
        var organizadorId = Guid.NewGuid();
        var cartonId = Guid.NewGuid();

        var items = new List<ItemCarrito> { new(cartonId, 100m) };

        var carritoRepository = new Mock<ICarritoRepository>(MockBehavior.Strict);
        var bingoRepository = new Mock<IBingoRepository>();
        var compraRepository = new Mock<ICompraRepository>();

        var secuencia = new MockSequence();

        carritoRepository.Setup(r => r.ObtenerItemsAsync(sesionId)).ReturnsAsync(items);
        carritoRepository
            .Setup(r => r.RevalidarReservasAsync(sesionId))
            .ReturnsAsync(new RevalidacionCarrito(true, items, Array.Empty<Guid>()));
        bingoRepository
            .Setup(r => r.ObtenerParaConfirmarCompraAsync(It.IsAny<IReadOnlyCollection<Guid>>()))
            .ReturnsAsync(new List<CartonParaConfirmarCompra>
            {
                new(cartonId, Guid.NewGuid(), organizadorId, "Club X", "Bingo X"),
            });

        compraRepository
            .InSequence(secuencia)
            .Setup(r => r.CrearVariasAsync(It.IsAny<IReadOnlyList<Domain.Compras.Compra>>()))
            .Returns(Task.CompletedTask);
        carritoRepository
            .InSequence(secuencia)
            .Setup(r => r.LiberarCarritoConfirmadoAsync(sesionId, It.IsAny<IReadOnlyCollection<Guid>>()))
            .Returns(Task.CompletedTask);

        var service = CrearService(carritoRepository, bingoRepository, compraRepository);

        await service.ConfirmarCompraAsync(sesionId, compradorId, MedioPago.Efectivo);

        // MockSequence (Moq) rechaza la llamada si no respeta el orden configurado — si
        // LiberarCarritoConfirmadoAsync se invocara ANTES de CrearVariasAsync, el setup no
        // matchearía y Moq lanzaría MockException al invocar el método, por lo que llegar hasta
        // acá sin excepción ya prueba el orden.
        compraRepository.Verify(r => r.CrearVariasAsync(It.IsAny<IReadOnlyList<Domain.Compras.Compra>>()), Times.Once());
        carritoRepository.Verify(
            r => r.LiberarCarritoConfirmadoAsync(sesionId, It.IsAny<IReadOnlyCollection<Guid>>()),
            Times.Once());
    }

    [Fact]
    public async Task ConfirmarCompraAsync_ConCrearVariasAsyncLanzandoReservaCarritoInvalidaException_PropagaLaExcepcionYNuncaInvocaLiberarCarritoConfirmadoAsync()
    {
        var sesionId = Guid.NewGuid().ToString();
        var compradorId = Guid.NewGuid();
        var organizadorId = Guid.NewGuid();
        var cartonId = Guid.NewGuid();

        var items = new List<ItemCarrito> { new(cartonId, 100m) };

        var carritoRepository = new Mock<ICarritoRepository>();
        var bingoRepository = new Mock<IBingoRepository>();
        var compraRepository = new Mock<ICompraRepository>();

        carritoRepository.Setup(r => r.ObtenerItemsAsync(sesionId)).ReturnsAsync(items);
        carritoRepository
            .Setup(r => r.RevalidarReservasAsync(sesionId))
            .ReturnsAsync(new RevalidacionCarrito(true, items, Array.Empty<Guid>()));
        bingoRepository
            .Setup(r => r.ObtenerParaConfirmarCompraAsync(It.IsAny<IReadOnlyCollection<Guid>>()))
            .ReturnsAsync(new List<CartonParaConfirmarCompra>
            {
                new(cartonId, Guid.NewGuid(), organizadorId, "Club X", "Bingo X"),
            });
        // Corrective round 2: la traducción DbUpdateException -> ReservaCarritoInvalidaException
        // ahora vive en Infrastructure (CompraRepository), no en CompraService — Application no
        // puede depender de Microsoft.EntityFrameworkCore (AGENTS.md, "Layer separation"). Este test
        // ahora verifica que CompraService simplemente deja propagar la excepción de Domain que
        // CrearVariasAsync ya lanza directamente, sin capturarla ni traducirla.
        compraRepository
            .Setup(r => r.CrearVariasAsync(It.IsAny<IReadOnlyList<Domain.Compras.Compra>>()))
            .ThrowsAsync(new ReservaCarritoInvalidaException(
                Array.Empty<Guid>(),
                "La confirmación perdió la carrera contra otra compra concurrente."));

        var service = CrearService(carritoRepository, bingoRepository, compraRepository);

        await Assert.ThrowsAsync<ReservaCarritoInvalidaException>(() =>
            service.ConfirmarCompraAsync(sesionId, compradorId, MedioPago.Efectivo));

        carritoRepository.Verify(
            r => r.LiberarCarritoConfirmadoAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<Guid>>()),
            Times.Never());
    }
}
