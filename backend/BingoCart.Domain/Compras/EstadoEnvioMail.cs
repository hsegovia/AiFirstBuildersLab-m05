namespace BingoCart.Domain.Compras;

/// <summary>
/// Estados del ciclo de vida de un <see cref="EnvioMail"/> (spec FEAT-009b, Block 1).
/// </summary>
public enum EstadoEnvioMail
{
    Pendiente,
    Exitoso,
    Fallido,
}
