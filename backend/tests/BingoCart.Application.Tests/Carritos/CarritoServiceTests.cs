using BingoCart.Application.Bingos;
using BingoCart.Application.Carritos;
using BingoCart.Application.Carritos.Dtos;
using BingoCart.Application.Descubrimiento;
using BingoCart.Domain.Bingos;
using BingoCart.Domain.Carritos;
using BingoCart.Domain.Carritos.Exceptions;
using BingoCart.Domain.Organizadores.Exceptions;
using Moq;

namespace BingoCart.Application.Tests.Carritos;

/// <summary>
/// Tests unitarios de <see cref="CarritoService"/> (spec FEAT-008b, Block 2) — mocks de
/// <see cref="ICarritoRepository"/>/<see cref="IBingoRepository"/>/
/// <see cref="IDescubrimientoRepository"/>, mismo patrón que <c>DescubrimientoServiceTests</c>.
/// </summary>
public class CarritoServiceTests
{
    private static CarritoService CrearService(
        Mock<ICarritoRepository> carritoRepository,
        Mock<IBingoRepository> bingoRepository,
        Mock<IDescubrimientoRepository> descubrimientoRepository) =>
        new(carritoRepository.Object, bingoRepository.Object, descubrimientoRepository.Object, TimeProvider.System);

    [Fact]
    public async Task AgregarAsync_ConCartonValidoYReservaExitosa_NoLanzaEInvocaAmbosRepositoriosConLosParametrosCorrectos()
    {
        var sesionId = Guid.NewGuid().ToString();
        var cartonId = Guid.NewGuid();
        var cartonInfo = new CartonParaCarrito(cartonId, Guid.NewGuid(), 150m, "Club X", "Bingo X");

        var carritoRepository = new Mock<ICarritoRepository>();
        var bingoRepository = new Mock<IBingoRepository>();
        var descubrimientoRepository = new Mock<IDescubrimientoRepository>();

        bingoRepository
            .Setup(r => r.ObtenerParaCarritoAsync(cartonId, It.IsAny<DateTime>()))
            .ReturnsAsync(cartonInfo);
        carritoRepository
            .Setup(r => r.IntentarAgregarAsync(sesionId, cartonId, 150m, TimeSpan.FromMinutes(5)))
            .ReturnsAsync(true);

        var service = CrearService(carritoRepository, bingoRepository, descubrimientoRepository);

        await service.AgregarAsync(sesionId, cartonId);

        bingoRepository.Verify(r => r.ObtenerParaCarritoAsync(cartonId, It.IsAny<DateTime>()), Times.Once());
        carritoRepository.Verify(
            r => r.IntentarAgregarAsync(sesionId, cartonId, 150m, TimeSpan.FromMinutes(5)),
            Times.Once());
    }

    [Fact]
    public async Task AgregarAsync_ConCartonInexistenteOBingoVencido_LanzaCartonInexistenteExceptionSinInvocarIntentarAgregarAsync()
    {
        var sesionId = Guid.NewGuid().ToString();
        var cartonId = Guid.NewGuid();

        var carritoRepository = new Mock<ICarritoRepository>();
        var bingoRepository = new Mock<IBingoRepository>();
        var descubrimientoRepository = new Mock<IDescubrimientoRepository>();

        bingoRepository
            .Setup(r => r.ObtenerParaCarritoAsync(cartonId, It.IsAny<DateTime>()))
            .ReturnsAsync((CartonParaCarrito?)null);

        var service = CrearService(carritoRepository, bingoRepository, descubrimientoRepository);

        await Assert.ThrowsAsync<CartonInexistenteException>(() => service.AgregarAsync(sesionId, cartonId));

        carritoRepository.Verify(
            r => r.IntentarAgregarAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<TimeSpan>()),
            Times.Never());
    }

    [Fact]
    public async Task AgregarAsync_ConCartonValidoPeroYaReservadoPorOtraSesion_LanzaCartonYaReservadoException()
    {
        var sesionId = Guid.NewGuid().ToString();
        var cartonId = Guid.NewGuid();
        var cartonInfo = new CartonParaCarrito(cartonId, Guid.NewGuid(), 100m, "Club Y", "Bingo Y");

        var carritoRepository = new Mock<ICarritoRepository>();
        var bingoRepository = new Mock<IBingoRepository>();
        var descubrimientoRepository = new Mock<IDescubrimientoRepository>();

        bingoRepository
            .Setup(r => r.ObtenerParaCarritoAsync(cartonId, It.IsAny<DateTime>()))
            .ReturnsAsync(cartonInfo);
        carritoRepository
            .Setup(r => r.IntentarAgregarAsync(sesionId, cartonId, 100m, It.IsAny<TimeSpan>()))
            .ReturnsAsync(false);

        var service = CrearService(carritoRepository, bingoRepository, descubrimientoRepository);

        await Assert.ThrowsAsync<CartonYaReservadoException>(() => service.AgregarAsync(sesionId, cartonId));
    }

    [Fact]
    public async Task ObtenerCarritoAsync_ConTresItemsDePrecios100200300_CantidadTotalYMontoTotalCorrectos()
    {
        var sesionId = Guid.NewGuid().ToString();
        var itemUno = new ItemCarrito(Guid.NewGuid(), 100m);
        var itemDos = new ItemCarrito(Guid.NewGuid(), 200m);
        var itemTres = new ItemCarrito(Guid.NewGuid(), 300m);

        var carritoRepository = new Mock<ICarritoRepository>();
        var bingoRepository = new Mock<IBingoRepository>();
        var descubrimientoRepository = new Mock<IDescubrimientoRepository>();

        carritoRepository
            .Setup(r => r.ObtenerItemsAsync(sesionId))
            .ReturnsAsync(new List<ItemCarrito> { itemUno, itemDos, itemTres });

        foreach (var item in new[] { itemUno, itemDos, itemTres })
        {
            bingoRepository
                .Setup(r => r.ObtenerParaCarritoAsync(item.CartonId, It.IsAny<DateTime>()))
                .ReturnsAsync(new CartonParaCarrito(item.CartonId, Guid.NewGuid(), item.PrecioUnitario, "Club", "Bingo"));
        }

        var service = CrearService(carritoRepository, bingoRepository, descubrimientoRepository);

        var response = await service.ObtenerCarritoAsync(sesionId);

        Assert.Equal(3, response.CantidadTotal);
        Assert.Equal(600m, response.MontoTotal);
        Assert.Equal(3, response.Items.Count);
    }

    [Fact]
    public async Task ObtenerCarritoAsync_ConCarritoVacio_CantidadTotalMontoTotalEItemsSonCero()
    {
        var sesionId = Guid.NewGuid().ToString();
        var carritoRepository = new Mock<ICarritoRepository>();
        var bingoRepository = new Mock<IBingoRepository>();
        var descubrimientoRepository = new Mock<IDescubrimientoRepository>();

        carritoRepository.Setup(r => r.ObtenerItemsAsync(sesionId)).ReturnsAsync(Array.Empty<ItemCarrito>());

        var service = CrearService(carritoRepository, bingoRepository, descubrimientoRepository);

        var response = await service.ObtenerCarritoAsync(sesionId);

        Assert.Equal(0, response.CantidadTotal);
        Assert.Equal(0m, response.MontoTotal);
        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task PedirNuevaTandaGlobalAsync_ConDosItemsEnCarritoYUnDescartadoPrevio_InvocaObtenerAleatoriosGlobalAsyncConLaUnionDeLosTres()
    {
        var sesionId = Guid.NewGuid().ToString();
        var cartonEnCarritoUno = Guid.NewGuid();
        var cartonEnCarritoDos = Guid.NewGuid();
        var cartonDescartadoPrevio = Guid.NewGuid();
        var nuevoDescartado = Guid.NewGuid();

        var carritoRepository = new Mock<ICarritoRepository>();
        var bingoRepository = new Mock<IBingoRepository>();
        var descubrimientoRepository = new Mock<IDescubrimientoRepository>();

        carritoRepository
            .Setup(r => r.ObtenerItemsAsync(sesionId))
            .ReturnsAsync(new List<ItemCarrito>
            {
                new(cartonEnCarritoUno, 100m),
                new(cartonEnCarritoDos, 200m),
            });
        carritoRepository
            .Setup(r => r.ObtenerDescartadosAsync(sesionId))
            .ReturnsAsync(new HashSet<Guid> { cartonDescartadoPrevio });
        descubrimientoRepository
            .Setup(r => r.ObtenerAleatoriosGlobalAsync(It.IsAny<DateTime>(), 5, It.IsAny<IReadOnlyCollection<Guid>>()))
            .ReturnsAsync(Array.Empty<Carton>());

        var service = CrearService(carritoRepository, bingoRepository, descubrimientoRepository);

        await service.PedirNuevaTandaGlobalAsync(sesionId, new[] { nuevoDescartado });

        carritoRepository.Verify(
            r => r.AgregarDescartadosAsync(
                sesionId,
                It.Is<IReadOnlyCollection<Guid>>(c => c.Count == 1 && c.Contains(nuevoDescartado)),
                TimeSpan.FromMinutes(30)),
            Times.Once());
        descubrimientoRepository.Verify(
            r => r.ObtenerAleatoriosGlobalAsync(
                It.IsAny<DateTime>(),
                5,
                It.Is<IReadOnlyCollection<Guid>>(ids =>
                    ids.Count == 3 &&
                    ids.Contains(cartonEnCarritoUno) &&
                    ids.Contains(cartonEnCarritoDos) &&
                    ids.Contains(cartonDescartadoPrevio))),
            Times.Once());
    }

    [Fact]
    public async Task PedirNuevaTandaGlobalAsync_Con51Descartados_LanzaCantidadDescartadosExcedeLimiteExceptionSinInvocarRepositorios()
    {
        // Corrección post-SAST (F-SAST-14): el límite debe cortar ANTES de tocar Redis o SQL —
        // ningún método de ICarritoRepository/IDescubrimientoRepository puede invocarse.
        var sesionId = Guid.NewGuid().ToString();
        var descartados = Enumerable.Range(0, 51).Select(_ => Guid.NewGuid()).ToList();

        var carritoRepository = new Mock<ICarritoRepository>();
        var bingoRepository = new Mock<IBingoRepository>();
        var descubrimientoRepository = new Mock<IDescubrimientoRepository>();

        var service = CrearService(carritoRepository, bingoRepository, descubrimientoRepository);

        await Assert.ThrowsAsync<CantidadDescartadosExcedeLimiteException>(
            () => service.PedirNuevaTandaGlobalAsync(sesionId, descartados));

        carritoRepository.VerifyNoOtherCalls();
        descubrimientoRepository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PedirNuevaTandaPorOrganizadorAsync_ConOrganizadorInexistente_LanzaOrganizadorNoEncontradoExceptionSinInvocarObtenerBingoActivoNiAleatorios()
    {
        // F-VER-04: rama nueva de este ticket (organizadorId sin organizador registrado) sin
        // ningún test que la ejerza — mismo patrón que
        // DescubrimientoServiceTests.DescubrirPorOrganizadorAsync_ConOrganizadorInexistente_...
        var sesionId = Guid.NewGuid().ToString();
        var organizadorId = Guid.NewGuid();

        var carritoRepository = new Mock<ICarritoRepository>();
        var bingoRepository = new Mock<IBingoRepository>();
        var descubrimientoRepository = new Mock<IDescubrimientoRepository>();

        descubrimientoRepository
            .Setup(r => r.ExisteOrganizadorAsync(organizadorId))
            .ReturnsAsync(false);

        var service = CrearService(carritoRepository, bingoRepository, descubrimientoRepository);

        await Assert.ThrowsAsync<OrganizadorNoEncontradoException>(
            () => service.PedirNuevaTandaPorOrganizadorAsync(sesionId, organizadorId, Array.Empty<Guid>()));

        descubrimientoRepository.Verify(
            r => r.ObtenerBingoActivoDeOrganizadorAsync(It.IsAny<Guid>(), It.IsAny<DateTime>()),
            Times.Never());
        descubrimientoRepository.Verify(
            r => r.ObtenerAleatoriosDeBingoAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<IReadOnlyCollection<Guid>>()),
            Times.Never());
    }

    [Fact]
    public async Task QuitarAsync_ConSesionIdYCartonId_DelegaDirectoAlRepositorioSinLanzar()
    {
        var sesionId = Guid.NewGuid().ToString();
        var cartonId = Guid.NewGuid();

        var carritoRepository = new Mock<ICarritoRepository>();
        var bingoRepository = new Mock<IBingoRepository>();
        var descubrimientoRepository = new Mock<IDescubrimientoRepository>();

        carritoRepository.Setup(r => r.QuitarAsync(sesionId, cartonId)).Returns(Task.CompletedTask);

        var service = CrearService(carritoRepository, bingoRepository, descubrimientoRepository);

        await service.QuitarAsync(sesionId, cartonId);

        carritoRepository.Verify(r => r.QuitarAsync(sesionId, cartonId), Times.Once());
    }
}
