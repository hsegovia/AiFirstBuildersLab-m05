using BingoCart.Application.Bingos;
using BingoCart.Application.Bingos.Dtos;
using BingoCart.Domain.Bingos;
using BingoCart.Domain.Bingos.Exceptions;
using Moq;

namespace BingoCart.Application.Tests.Bingos;

/// <summary>
/// Tests unitarios de <see cref="BingoService"/> (spec FEAT-003, Block 4) — mocks de
/// <see cref="IBingoRepository"/>/<see cref="ICartonNumberGenerator"/>, mismo patrón que
/// <c>OrganizadorServiceTests</c>.
/// </summary>
public class BingoServiceTests
{
    private static readonly Guid OrganizadorId = Guid.NewGuid();

    private static CrearBingoRequest CrearRequest(
        string nombreEvento = "Bingo de prueba",
        int cantidadCartones = 3,
        decimal costoPorCarton = 100m) =>
        new(nombreEvento, DateTime.UtcNow.AddDays(5), cantidadCartones, costoPorCarton);

    private static BingoService CrearService(
        Mock<IBingoRepository> repository,
        Mock<ICartonNumberGenerator> generador)
    {
        return new BingoService(repository.Object, generador.Object, TimeProvider.System);
    }

    private static IReadOnlyList<IReadOnlyList<int>> ConjuntosDePrueba(int cantidad)
    {
        var conjuntos = new List<IReadOnlyList<int>>(cantidad);
        for (var i = 0; i < cantidad; i++)
        {
            // Conjuntos distintos entre sí, desplazando el rango de arranque — suficiente para
            // pasar por Carton.Crear (10 números, 1-90, sin duplicados) en un test unitario que no
            // necesita CSPRNG real.
            var inicio = 1 + (i % 8) * 10;
            conjuntos.Add(Enumerable.Range(inicio, 10).ToList());
        }

        return conjuntos;
    }

    [Fact]
    public async Task CrearAsync_ConDatosValidos_DevuelveResponseCorrectoYPersisteLaCantidadCorrectaDeCartones()
    {
        var repository = new Mock<IBingoRepository>();
        repository.Setup(r => r.TieneBingoActivoAsync(OrganizadorId, It.IsAny<DateTime>()))
            .ReturnsAsync(false);

        var generador = new Mock<ICartonNumberGenerator>();
        generador.Setup(g => g.GenerarConjuntosUnicos(3)).Returns(ConjuntosDePrueba(3));

        Bingo? bingoCreado = null;
        IReadOnlyList<Carton>? cartonesCreados = null;
        repository
            .Setup(r => r.CrearAsync(It.IsAny<Bingo>(), It.IsAny<IReadOnlyList<Carton>>()))
            .Callback<Bingo, IReadOnlyList<Carton>>((b, c) =>
            {
                bingoCreado = b;
                cartonesCreados = c;
            })
            .Returns(Task.CompletedTask);

        var service = CrearService(repository, generador);
        var request = CrearRequest(nombreEvento: "Bingo Club", cantidadCartones: 3, costoPorCarton: 250m);

        var response = await service.CrearAsync(request, OrganizadorId);

        Assert.NotEqual(Guid.Empty, response.Id);
        Assert.Equal("Bingo Club", response.NombreEvento);
        Assert.Equal(request.FechaSorteoUtc, response.FechaSorteoUtc);
        Assert.Equal(3, response.CantidadCartones);
        Assert.Equal(250m, response.CostoPorCarton);

        repository.Verify(
            r => r.CrearAsync(It.IsAny<Bingo>(), It.Is<IReadOnlyList<Carton>>(c => c.Count == 3)),
            Times.Once());

        Assert.NotNull(bingoCreado);
        Assert.Equal(response.Id, bingoCreado!.Id);
        Assert.NotNull(cartonesCreados);
        Assert.All(cartonesCreados!, c => Assert.Equal(bingoCreado.Id, c.BingoId));
    }

    [Fact]
    public async Task CrearAsync_ConBingoActivoExistente_LanzaExcepcionSinLlamarAlGenerador()
    {
        var repository = new Mock<IBingoRepository>();
        repository.Setup(r => r.TieneBingoActivoAsync(OrganizadorId, It.IsAny<DateTime>()))
            .ReturnsAsync(true);

        var generador = new Mock<ICartonNumberGenerator>();

        var service = CrearService(repository, generador);

        await Assert.ThrowsAsync<BingoActivoExistenteException>(() =>
            service.CrearAsync(CrearRequest(), OrganizadorId));

        generador.Verify(g => g.GenerarConjuntosUnicos(It.IsAny<int>()), Times.Never());
        repository.Verify(
            r => r.CrearAsync(It.IsAny<Bingo>(), It.IsAny<IReadOnlyList<Carton>>()),
            Times.Never());
    }

    [Fact]
    public async Task ListarPropiosAsync_ConDatosValidos_DevuelveBingoListadoResponseCorrecto()
    {
        var ahoraUtc = DateTime.UtcNow;
        var bingoMasAntiguo = Bingo.Crear("Bingo A", ahoraUtc.AddDays(5), 10, 100m, OrganizadorId, ahoraUtc);
        var bingoMasReciente = Bingo.Crear(
            "Bingo B", ahoraUtc.AddDays(6), 20, 200m, OrganizadorId, ahoraUtc.AddMinutes(1));

        var repository = new Mock<IBingoRepository>();
        repository
            .Setup(r => r.ListarPorOrganizadorAsync(OrganizadorId, 1, 20))
            .ReturnsAsync(new BingosPaginados(new[] { bingoMasReciente, bingoMasAntiguo }, 2));

        var generador = new Mock<ICartonNumberGenerator>();
        var service = CrearService(repository, generador);

        var response = await service.ListarPropiosAsync(OrganizadorId, page: 1, pageSize: 20);

        Assert.Equal(2, response.Items.Count);
        Assert.Equal(bingoMasReciente.Id, response.Items[0].Id);
        Assert.Equal(bingoMasReciente.NombreEvento, response.Items[0].NombreEvento);
        Assert.Equal(bingoMasReciente.FechaSorteoUtc, response.Items[0].FechaSorteoUtc);
        Assert.Equal(bingoMasReciente.CantidadCartones, response.Items[0].CantidadCartones);
        Assert.Equal(bingoMasReciente.CostoPorCarton, response.Items[0].CostoPorCarton);
        Assert.Equal(bingoMasAntiguo.Id, response.Items[1].Id);
        Assert.Equal(2, response.Total);
        Assert.Equal(1, response.TotalPaginas);
        Assert.Equal(1, response.Page);
        Assert.Equal(20, response.PageSize);
    }

    [Fact]
    public async Task ListarPropiosAsync_ConPageSize500_InvocaAlRepositorioConPageSizeClampeadoA100()
    {
        var repository = new Mock<IBingoRepository>();
        repository
            .Setup(r => r.ListarPorOrganizadorAsync(OrganizadorId, 1, 100))
            .ReturnsAsync(new BingosPaginados(Array.Empty<Bingo>(), 0));

        var generador = new Mock<ICartonNumberGenerator>();
        var service = CrearService(repository, generador);

        await service.ListarPropiosAsync(OrganizadorId, page: 1, pageSize: 500);

        repository.Verify(r => r.ListarPorOrganizadorAsync(OrganizadorId, 1, 100), Times.Once());
    }

    [Fact]
    public async Task ListarPropiosAsync_ConOrganizadorSinBingos_DevuelveItemsVacioYTotalCero()
    {
        var repository = new Mock<IBingoRepository>();
        repository
            .Setup(r => r.ListarPorOrganizadorAsync(OrganizadorId, 1, 20))
            .ReturnsAsync(new BingosPaginados(Array.Empty<Bingo>(), 0));

        var generador = new Mock<ICartonNumberGenerator>();
        var service = CrearService(repository, generador);

        var response = await service.ListarPropiosAsync(OrganizadorId, page: 1, pageSize: 20);

        Assert.Empty(response.Items);
        Assert.Equal(0, response.Total);
        Assert.Equal(0, response.TotalPaginas);
    }

    private static EditarBingoRequest EditarRequest(
        string nombreEvento = "Bingo editado",
        decimal costoPorCarton = 150m) =>
        new(nombreEvento, DateTime.UtcNow.AddDays(10), costoPorCarton);

    private static Bingo BingoDePrueba(Guid organizadorId, DateTime? ahoraUtc = null) =>
        Bingo.Crear(
            "Bingo original",
            (ahoraUtc ?? DateTime.UtcNow).AddDays(5),
            3,
            100m,
            organizadorId,
            ahoraUtc ?? DateTime.UtcNow);

    [Fact]
    public async Task EditarAsync_ConDatosValidosYBingoPropioSinCompras_DevuelveResponseActualizadoYGuardaCambios()
    {
        var bingo = BingoDePrueba(OrganizadorId);

        var repository = new Mock<IBingoRepository>();
        repository.Setup(r => r.ObtenerPorIdAsync(bingo.Id)).ReturnsAsync(bingo);
        repository.Setup(r => r.TieneComprasRegistradasAsync(bingo.Id)).ReturnsAsync(false);
        repository.Setup(r => r.GuardarCambiosAsync()).Returns(Task.CompletedTask);

        var generador = new Mock<ICartonNumberGenerator>();
        var service = CrearService(repository, generador);
        var request = EditarRequest(nombreEvento: "Bingo Club Editado", costoPorCarton: 300m);

        var response = await service.EditarAsync(bingo.Id, request, OrganizadorId);

        Assert.Equal(bingo.Id, response.Id);
        Assert.Equal("Bingo Club Editado", response.NombreEvento);
        Assert.Equal(request.FechaSorteoUtc, response.FechaSorteoUtc);
        Assert.Equal(3, response.CantidadCartones);
        Assert.Equal(300m, response.CostoPorCarton);

        repository.Verify(r => r.GuardarCambiosAsync(), Times.Once());
    }

    [Fact]
    public async Task EditarAsync_ConIdInexistente_LanzaBingoNoEncontradoException()
    {
        var repository = new Mock<IBingoRepository>();
        repository.Setup(r => r.ObtenerPorIdAsync(It.IsAny<Guid>())).ReturnsAsync((Bingo?)null);

        var generador = new Mock<ICartonNumberGenerator>();
        var service = CrearService(repository, generador);

        await Assert.ThrowsAsync<BingoNoEncontradoException>(() =>
            service.EditarAsync(Guid.NewGuid(), EditarRequest(), OrganizadorId));
    }

    [Fact]
    public async Task EditarAsync_ConBingoDeOtroOrganizador_LanzaBingoNoEncontradoException()
    {
        var otroOrganizadorId = Guid.NewGuid();
        var bingo = BingoDePrueba(otroOrganizadorId);

        var repository = new Mock<IBingoRepository>();
        repository.Setup(r => r.ObtenerPorIdAsync(bingo.Id)).ReturnsAsync(bingo);

        var generador = new Mock<ICartonNumberGenerator>();
        var service = CrearService(repository, generador);

        // Mismo tipo de excepción que el Id inexistente (no-enumeración) — verificado explícitamente.
        await Assert.ThrowsAsync<BingoNoEncontradoException>(() =>
            service.EditarAsync(bingo.Id, EditarRequest(), OrganizadorId));
    }

    [Fact]
    public async Task EditarAsync_ConComprasRegistradas_LanzaBingoConComprasException()
    {
        var bingo = BingoDePrueba(OrganizadorId);

        var repository = new Mock<IBingoRepository>();
        repository.Setup(r => r.ObtenerPorIdAsync(bingo.Id)).ReturnsAsync(bingo);
        repository.Setup(r => r.TieneComprasRegistradasAsync(bingo.Id)).ReturnsAsync(true);

        var generador = new Mock<ICartonNumberGenerator>();
        var service = CrearService(repository, generador);

        await Assert.ThrowsAsync<BingoConComprasException>(() =>
            service.EditarAsync(bingo.Id, EditarRequest(), OrganizadorId));

        repository.Verify(r => r.GuardarCambiosAsync(), Times.Never());
    }

    [Fact]
    public async Task EliminarAsync_ConBingoPropioSinCompras_InvocaEliminarAsyncConElBingoCorrecto()
    {
        var bingo = BingoDePrueba(OrganizadorId);

        var repository = new Mock<IBingoRepository>();
        repository.Setup(r => r.ObtenerPorIdAsync(bingo.Id)).ReturnsAsync(bingo);
        repository.Setup(r => r.TieneComprasRegistradasAsync(bingo.Id)).ReturnsAsync(false);
        repository.Setup(r => r.EliminarAsync(bingo)).Returns(Task.CompletedTask);

        var generador = new Mock<ICartonNumberGenerator>();
        var service = CrearService(repository, generador);

        await service.EliminarAsync(bingo.Id, OrganizadorId);

        repository.Verify(r => r.EliminarAsync(bingo), Times.Once());
    }

    [Fact]
    public async Task EliminarAsync_ConIdInexistente_LanzaBingoNoEncontradoException()
    {
        var repository = new Mock<IBingoRepository>();
        repository.Setup(r => r.ObtenerPorIdAsync(It.IsAny<Guid>())).ReturnsAsync((Bingo?)null);

        var generador = new Mock<ICartonNumberGenerator>();
        var service = CrearService(repository, generador);

        await Assert.ThrowsAsync<BingoNoEncontradoException>(() =>
            service.EliminarAsync(Guid.NewGuid(), OrganizadorId));
    }

    [Fact]
    public async Task EliminarAsync_ConComprasRegistradas_LanzaBingoConComprasException()
    {
        var bingo = BingoDePrueba(OrganizadorId);

        var repository = new Mock<IBingoRepository>();
        repository.Setup(r => r.ObtenerPorIdAsync(bingo.Id)).ReturnsAsync(bingo);
        repository.Setup(r => r.TieneComprasRegistradasAsync(bingo.Id)).ReturnsAsync(true);

        var generador = new Mock<ICartonNumberGenerator>();
        var service = CrearService(repository, generador);

        await Assert.ThrowsAsync<BingoConComprasException>(() =>
            service.EliminarAsync(bingo.Id, OrganizadorId));

        repository.Verify(r => r.EliminarAsync(It.IsAny<Bingo>()), Times.Never());
    }
}
