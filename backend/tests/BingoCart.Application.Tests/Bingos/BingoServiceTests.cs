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
}
