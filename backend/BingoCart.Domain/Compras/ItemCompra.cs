namespace BingoCart.Domain.Compras;

/// <summary>
/// Un ítem de una <see cref="Compra"/> confirmada: un cartón junto con el precio con el que se
/// vendió — snapshot tomado de <c>ItemCarrito.PrecioUnitario</c> (Redis, FEAT-008b) al momento de
/// confirmar, nunca releído de SQL (decisión de PLAN: evita que el monto cambie entre agregar al
/// carrito y confirmar).
/// </summary>
public sealed record ItemCompra(Guid CartonId, decimal PrecioUnitario);
