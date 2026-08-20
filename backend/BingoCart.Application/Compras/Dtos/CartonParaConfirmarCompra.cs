namespace BingoCart.Application.Compras.Dtos;

/// <summary>
/// Datos de un cartón resueltos contra SQL Server al confirmar una compra (spec FEAT-009a, Block 1)
/// — a diferencia de <c>CartonParaCarrito</c> (FEAT-008b), no incluye precio: el precio de cada ítem
/// se toma del snapshot ya guardado en Redis (<c>ItemCarrito.PrecioUnitario</c>), nunca se vuelve a
/// leer de SQL en este punto (evita que el monto cambie entre agregar al carrito y confirmar).
/// </summary>
public sealed record CartonParaConfirmarCompra(
    Guid CartonId,
    Guid BingoId,
    Guid OrganizadorId,
    string NombreOrganizacion,
    string NombreEvento);
