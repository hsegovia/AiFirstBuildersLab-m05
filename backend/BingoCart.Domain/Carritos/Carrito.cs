namespace BingoCart.Domain.Carritos;

/// <summary>
/// Agregado puro, sin I/O (spec FEAT-008b, Block 1): aritmética sobre una lista de
/// <see cref="ItemCarrito"/> ya cargada — no decide qué está o no reservado, eso lo resuelve Redis
/// vía el script Lua de <c>CarritoRepository</c> antes de que exista un <see cref="Carrito"/> en
/// memoria. Valida FR-09 (recalcular cantidad y monto en cada agregado/quitado).
/// </summary>
public sealed class Carrito
{
    public IReadOnlyList<ItemCarrito> Items { get; }

    public int CantidadTotal => Items.Count;

    public decimal MontoTotal => Items.Sum(i => i.PrecioUnitario);

    private Carrito(IReadOnlyList<ItemCarrito> items) => Items = items;

    public static Carrito DeItems(IReadOnlyList<ItemCarrito> items) => new(items);
}
