using BingoCart.Application.Compras;
using BingoCart.Application.Compras.Dtos;
using BingoCart.Domain.Bingos;
using Moq;

namespace BingoCart.Application.Tests.Compras;

/// <summary>
/// Tests unitarios de <see cref="MisCartonesService"/> (spec FEAT-009d, Block 3) — mock de
/// <see cref="ICompraRepository"/>, sin dependencia real de EF Core. Mismo patrón que
/// <see cref="CompraOrganizadorServiceTests"/> (clamp de <c>pageSize</c>).
/// </summary>
public sealed class MisCartonesServiceTests
{
    private static CartonAdquiridoResponse NuevoCartonAdquirido(int numeroCorrelativo) =>
        new(
            Guid.NewGuid(),
            numeroCorrelativo,
            Enumerable.Range(1, 10).ToList(),
            "Bingo de prueba",
            "Club de prueba",
            "Confirmado",
            Guid.NewGuid());

    [Fact]
    public async Task ListarAsync_ConPageSizeMayorA50_ClampeaA50YDevuelveElTotalSinPaginar()
    {
        var compradorId = Guid.NewGuid();
        var itemsDeLaPagina = Enumerable.Range(1, 50).Select(NuevoCartonAdquirido).ToList();

        var compraRepository = new Mock<ICompraRepository>();
        compraRepository
            .Setup(r => r.ListarCartonesDelCompradorAsync(compradorId, 1, 50))
            .ReturnsAsync(new CartonesAdquiridosPaginados(itemsDeLaPagina, 120));

        var service = new MisCartonesService(compraRepository.Object, Mock.Of<ICartonPdfRenderer>());

        var response = await service.ListarAsync(compradorId, page: 1, pageSize: 200);

        // NFR-01/AC-03: pageSize se clampea a 50 antes de llegar al repositorio (el mock solo
        // responde al Setup con 1, 50 — si el service pasara 200 sin clampear, Moq devolvería el
        // default de CartonesAdquiridosPaginados, que es null, y el await siguiente lanzaría).
        Assert.Equal(50, response.PageSize);
        // El total real (sin paginar) se devuelve completo, no acotado al tamaño de la página.
        Assert.Equal(120, response.Total);
        Assert.Equal(50, response.Items.Count);
        compraRepository.Verify(r => r.ListarCartonesDelCompradorAsync(compradorId, 1, 50), Times.Once());
    }

    // Spec FEAT-009d, Block 4 — sad path del 500 genérico: una falla del renderer no se traga ni se
    // convierte en un 200 vacío, se propaga tal cual (sin envolverla ni filtrar su mensaje) para que
    // ExceptionHandlingMiddleware la capture con su catch genérico.
    [Fact]
    public async Task ObtenerPdfAsync_ConRendererQueFalla_PropagaLaExcepcionSinFiltrarDetalles()
    {
        var compradorId = Guid.NewGuid();
        var cartonId = Guid.NewGuid();
        var carton = Carton.Crear(Guid.NewGuid(), Enumerable.Range(1, 10).ToList(), numeroCorrelativo: 7);

        var compraRepository = new Mock<ICompraRepository>();
        compraRepository
            .Setup(r => r.ObtenerCartonDelCompradorAsync(compradorId, cartonId))
            .ReturnsAsync(carton);

        var cartonPdfRenderer = new Mock<ICartonPdfRenderer>();
        cartonPdfRenderer
            .Setup(r => r.Renderizar(carton.Id, carton.NumeroCorrelativo, carton.Numeros))
            .Throws(new InvalidOperationException("Fallo simulado del renderer."));

        var service = new MisCartonesService(compraRepository.Object, cartonPdfRenderer.Object);

        var excepcion = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ObtenerPdfAsync(compradorId, cartonId));
        Assert.Equal("Fallo simulado del renderer.", excepcion.Message);
    }
}
