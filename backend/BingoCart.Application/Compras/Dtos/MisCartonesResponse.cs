namespace BingoCart.Application.Compras.Dtos;

/// <summary>
/// Respuesta paginada del listado "mis cartones" del comprador autenticado (spec FEAT-009d,
/// Block 3) — mismo shape exacto que <see cref="CompraListadoResponse"/>. <c>PageSize</c> es el
/// valor CLAMPEADO realmente aplicado (máximo 50, NFR-01), no el solicitado.
/// </summary>
public sealed record MisCartonesResponse(
    IReadOnlyList<CartonAdquiridoResponse> Items,
    int Total,
    int TotalPaginas,
    int Page,
    int PageSize);
