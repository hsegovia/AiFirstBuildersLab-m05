namespace BingoCart.Application.Compras.Dtos;

/// <summary>
/// Response de confirmar una compra (spec FEAT-009a, Block 2) — una <see cref="CompraCreada"/> por
/// organizador cuyos cartones estaban en el carrito confirmado.
/// </summary>
public sealed record ConfirmarCompraResponse(IReadOnlyList<CompraCreada> Compras);
