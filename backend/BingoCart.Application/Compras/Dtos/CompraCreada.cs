namespace BingoCart.Application.Compras.Dtos;

/// <summary>
/// Una <c>Compra</c> ya persistida, resultado de agrupar el carrito por organizador (spec FEAT-009a,
/// Block 2). <c>MontoTotal</c> es la suma de <c>PrecioUnitario</c> de todos sus ítems.
/// </summary>
public sealed record CompraCreada(
    Guid CompraId,
    Guid OrganizadorId,
    string NombreOrganizacion,
    int CantidadCartones,
    decimal MontoTotal);
