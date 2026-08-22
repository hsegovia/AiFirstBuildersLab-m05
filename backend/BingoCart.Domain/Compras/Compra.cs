namespace BingoCart.Domain.Compras;

/// <summary>
/// Entidad de dominio: una compra confirmada para un único organizador (FEAT-009a). Cuando el
/// carrito de una sesión mezcla cartones de varios organizadores, Application (Block 2) agrupa y
/// crea una <see cref="Compra"/> por organizador — este agregado nunca conoce esa agrupación, solo
/// representa una compra ya resuelta a un único <see cref="OrganizadorId"/>.
/// </summary>
public sealed class Compra
{
    public Guid Id { get; private init; }

    public Guid OrganizadorId { get; private init; }

    public Guid CompradorId { get; private init; }

    /// <summary>
    /// Agrupa todas las <see cref="Compra"/> generadas por una misma confirmación de carrito
    /// (FEAT-009b) — cuando el carrito mezcla cartones de varios organizadores, todas las
    /// <see cref="Compra"/> resultantes comparten este valor, para armar un único mail de
    /// confirmación en lugar de uno por organizador.
    /// </summary>
    public Guid ConfirmacionId { get; private init; }

    public IReadOnlyList<ItemCompra> Items { get; private init; } = Array.Empty<ItemCompra>();

    public MedioPago MedioPago { get; private init; }

    public EstadoCompra Estado { get; private init; }

    public DateTime FechaCreacionUtc { get; private init; }

    private Compra()
    {
    }

    /// <summary>
    /// Crea una <see cref="Compra"/> en estado <see cref="EstadoCompra.PendienteConfirmacionPago"/>.
    /// Valida <c>items.Count &gt; 0</c> como invariante interna de defensa en profundidad — la
    /// validación real de "carrito no vacío" (FR-04/AC-02) ya ocurre antes, en Application (Block 2),
    /// vía <c>CarritoVacioException</c>.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="items"/> está vacío.</exception>
    public static Compra Crear(
        Guid organizadorId,
        Guid compradorId,
        Guid confirmacionId,
        IReadOnlyList<ItemCompra> items,
        MedioPago medioPago,
        DateTime ahoraUtc)
    {
        if (items.Count == 0)
        {
            throw new ArgumentException("Una compra debe tener al menos un ítem.", nameof(items));
        }

        return new Compra
        {
            Id = Guid.NewGuid(),
            OrganizadorId = organizadorId,
            CompradorId = compradorId,
            ConfirmacionId = confirmacionId,
            Items = items,
            MedioPago = medioPago,
            Estado = EstadoCompra.PendienteConfirmacionPago,
            FechaCreacionUtc = ahoraUtc,
        };
    }
}
