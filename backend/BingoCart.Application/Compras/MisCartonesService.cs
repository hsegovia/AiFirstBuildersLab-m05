using BingoCart.Application.Compras.Dtos;

namespace BingoCart.Application.Compras;

/// <summary>
/// Implementa <see cref="IMisCartonesService"/> (spec FEAT-009d, Block 3) — mismo patrón de clamp de
/// <c>pageSize</c> y cálculo de <c>TotalPaginas</c> (con guarda de división por cero) que
/// <see cref="CompraOrganizadorService.ListarPropiasAsync"/>.
/// </summary>
public sealed class MisCartonesService : IMisCartonesService
{
    private const int PageSizeMaximo = 50;

    private readonly ICompraRepository _compraRepository;

    public MisCartonesService(ICompraRepository compraRepository)
    {
        _compraRepository = compraRepository;
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
}
