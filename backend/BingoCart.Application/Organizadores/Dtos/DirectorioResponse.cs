namespace BingoCart.Application.Organizadores.Dtos;

/// <summary>
/// Respuesta paginada del directorio público de organizadores (spec FEAT-005, Block 2). Mismo
/// patrón de forma que <see cref="Bingos.Dtos.BingoListadoResponse"/>: <c>PageSize</c> es el valor
/// CLAMPEADO realmente aplicado (máximo 100), no el solicitado.
/// </summary>
public sealed record DirectorioResponse(
    IReadOnlyList<DirectorioOrganizadorItem> Items,
    int Total,
    int TotalPaginas,
    int Page,
    int PageSize);
