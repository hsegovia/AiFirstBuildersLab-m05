namespace BingoCart.Application.Bingos.Dtos;

/// <summary>
/// Respuesta paginada del listado de bingos propios del organizador (FR-01, FR-02, FR-05 de
/// <c>prd-FEAT-004.md</c>). Reutiliza <see cref="BingoCreadoResponse"/> por ítem, sin renombrar
/// (decisión cerrada en PLAN, ver spec FEAT-004): mismos 5 campos exactos que el bloque de
/// creación, el PRD confirma en su sección "Out of Scope" que el listado no necesita datos
/// adicionales de ventas/estado por ahora — YAGNI, se separan en un ticket futuro si divergen.
/// <c>PageSize</c> es el valor CLAMPEADO realmente aplicado (máximo 100), no el solicitado, para
/// que la respuesta sea honesta sobre qué tamaño de página se aplicó.
/// </summary>
public sealed record BingoListadoResponse(
    IReadOnlyList<BingoCreadoResponse> Items,
    int Total,
    int TotalPaginas,
    int Page,
    int PageSize);
