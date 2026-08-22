namespace BingoCart.Application.Compras.Dtos;

/// <summary>
/// Ítem del listado de compras propias del organizador (spec FEAT-009c, Block 2, FR-07/AC-07) —
/// deliberadamente sin ningún dato del comprador (nombre/apellido/mail): el organizador ve el
/// estado y el monto de sus compras, no la identidad de quien compró.
/// </summary>
public sealed record CompraResumenResponse(
    Guid CompraId,
    string Estado,
    decimal MontoTotal,
    DateTime FechaCreacionUtc);
