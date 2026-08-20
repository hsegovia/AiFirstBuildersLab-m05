namespace BingoCart.Application.Carritos.Dtos;

/// <summary>
/// Un ítem del carrito ya enriquecido con los datos públicos de su bingo, listo para exponerse por
/// <c>GET /api/carrito</c> (spec FEAT-008b, Block 2/3) — a diferencia de
/// <see cref="Domain.Carritos.ItemCarrito"/> (Domain, Block 1), que solo trae <c>CartonId</c>/
/// <c>PrecioUnitario</c> porque Redis no guarda nombres.
/// </summary>
public sealed record ItemCarritoResponse(
    Guid CartonId,
    string NombreOrganizacion,
    string NombreEvento,
    decimal PrecioUnitario);
