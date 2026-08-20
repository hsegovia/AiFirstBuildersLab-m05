namespace BingoCart.Domain.Carritos;

/// <summary>
/// Un ítem del carrito: un cartón reservado junto con el precio con el que se agregó — snapshot
/// (decisión de PLAN, spec FEAT-008b): si el organizador edita <c>CostoPorCarton</c> mientras el
/// cartón está en un carrito ajeno, este valor no se recalcula.
/// </summary>
public sealed record ItemCarrito(Guid CartonId, decimal PrecioUnitario);
