namespace BingoCart.Domain.Compras;

/// <summary>
/// Discrimina el tipo de mail que representa un <see cref="EnvioMail"/> en el outbox (FEAT-009c,
/// Block 1): confirmación de compra (FEAT-009b) o cancelación (FEAT-009c). Determina qué referencia
/// (<see cref="EnvioMail.ConfirmacionId"/> o <see cref="EnvioMail.CompraId"/>) está seteada.
/// </summary>
public enum TipoEnvioMail
{
    Confirmacion,
    Cancelacion,
}
