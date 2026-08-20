namespace BingoCart.Domain.Compras;

/// <summary>
/// Estados del ciclo de vida de una <see cref="Compra"/>. Este ticket (FEAT-009a) solo crea compras
/// en <see cref="PendienteConfirmacionPago"/>; las transiciones a <see cref="Confirmado"/>/
/// <see cref="Cancelado"/> son FEAT-009c — el enum completo se define acá para no migrar el esquema
/// dos veces (decisión de PLAN).
/// </summary>
public enum EstadoCompra
{
    PendienteConfirmacionPago,
    Confirmado,
    Cancelado,
}
