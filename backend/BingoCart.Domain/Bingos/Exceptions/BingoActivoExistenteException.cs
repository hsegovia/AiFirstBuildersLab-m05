using BingoCart.Domain.Common;

namespace BingoCart.Domain.Bingos.Exceptions;

/// <summary>
/// Se lanza cuando el organizador ya tiene un bingo activo (con sorteo vigente) y no puede crear
/// otro (FR-06). Definida en Domain por consistencia con el resto de excepciones de negocio, pero
/// la lanza `BingoService` (Application, Block 4) — este bloque (dominio puro) no la lanza, mismo
/// criterio que `MailYaRegistradoException` en `Organizador`.
/// </summary>
public sealed class BingoActivoExistenteException : DomainException
{
    public BingoActivoExistenteException(string message)
        : base(message)
    {
    }
}
