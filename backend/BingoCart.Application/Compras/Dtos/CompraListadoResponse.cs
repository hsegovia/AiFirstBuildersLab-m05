namespace BingoCart.Application.Compras.Dtos;

/// <summary>
/// Respuesta paginada del listado de compras propias del organizador (spec FEAT-009c, Block 2) —
/// mismo shape exacto que <see cref="BingoCart.Application.Bingos.Dtos.BingoListadoResponse"/>.
/// <c>PageSize</c> es el valor CLAMPEADO realmente aplicado (máximo 50, NFR-01), no el solicitado.
/// </summary>
public sealed record CompraListadoResponse(
    IReadOnlyList<CompraResumenResponse> Items,
    int Total,
    int TotalPaginas,
    int Page,
    int PageSize);
