using BingoCart.Application.Compras.Dtos;
using BingoCart.Domain.Compras.Exceptions;

namespace BingoCart.Application.Compras;

/// <summary>
/// Implementa <see cref="IMisCartonesService"/> (spec FEAT-009d, Block 3 + Block 4). <see cref="ListarAsync"/>
/// sigue el mismo patrón de clamp de <c>pageSize</c> y cálculo de <c>TotalPaginas</c> (con guarda de
/// división por cero) que <see cref="CompraOrganizadorService.ListarPropiasAsync"/>.
/// <see cref="ObtenerPdfAsync"/> (Block 4) reutiliza <see cref="ICartonPdfRenderer"/> tal cual — este
/// ticket no construye generación de PDF, solo la expone on-demand.
/// </summary>
public sealed class MisCartonesService : IMisCartonesService
{
    private const int PageSizeMaximo = 50;

    private readonly ICompraRepository _compraRepository;
    private readonly ICartonPdfRenderer _cartonPdfRenderer;

    public MisCartonesService(ICompraRepository compraRepository, ICartonPdfRenderer cartonPdfRenderer)
    {
        _compraRepository = compraRepository;
        _cartonPdfRenderer = cartonPdfRenderer;
    }

    public async Task<MisCartonesResponse> ListarAsync(Guid compradorId, int page, int pageSize)
    {
        // NFR-01/AC-03: PageSize > 50 no se rechaza, se sirve con 50 — mismo criterio que
        // CompraOrganizadorService.ListarPropiasAsync.
        var pageSizeClamped = Math.Min(pageSize, PageSizeMaximo);

        var paginados = await _compraRepository.ListarCartonesDelCompradorAsync(compradorId, page, pageSizeClamped);

        var totalPaginas = paginados.Total == 0
            ? 0
            : (int)Math.Ceiling(paginados.Total / (double)pageSizeClamped);

        return new MisCartonesResponse(paginados.Items, paginados.Total, totalPaginas, page, pageSizeClamped);
    }

    public async Task<byte[]> ObtenerPdfAsync(Guid compradorId, Guid cartonId)
    {
        var carton = await _compraRepository.ObtenerCartonDelCompradorAsync(compradorId, cartonId);
        if (carton is null)
        {
            // Anti-enumeración (R-01, precedente ObtenerCompraPropiaAsync de FEAT-009c): mensaje
            // ESTÁTICO, sin interpolar cartonId ni ningún otro dato de la request — así la respuesta
            // es byte a byte idéntica tanto si el cartón no existe como si pertenece a otro
            // comprador, y no se puede enumerar cartones ajenos por diferencia de respuesta.
            throw new CartonNoEncontradoException("El cartón indicado no existe.");
        }

        return _cartonPdfRenderer.Renderizar(carton.Id, carton.NumeroCorrelativo, carton.Numeros);
    }
}
