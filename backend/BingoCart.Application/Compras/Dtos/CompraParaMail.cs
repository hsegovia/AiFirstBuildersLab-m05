namespace BingoCart.Application.Compras.Dtos;

/// <summary>
/// Detalle de una <c>Compra</c> a incluir en el mail de confirmación (spec FEAT-009b, Block 2):
/// nombre de organización, ID de compra, monto total y cartones (AC-02).
/// </summary>
public sealed record CompraParaMail(
    Guid CompraId,
    string NombreOrganizacion,
    decimal MontoTotal,
    IReadOnlyList<CartonParaMail> Cartones);
