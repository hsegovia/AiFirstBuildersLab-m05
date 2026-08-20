namespace BingoCart.Domain.Compras;

/// <summary>
/// Medios de pago aceptados por el sistema (FR-03) — exactamente dos, uno por confirmación de
/// carrito.
/// </summary>
public enum MedioPago
{
    Efectivo,
    Transferencia,
}
