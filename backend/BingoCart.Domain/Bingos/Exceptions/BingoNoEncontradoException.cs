using BingoCart.Domain.Common;

namespace BingoCart.Domain.Bingos.Exceptions;

/// <summary>
/// Se lanza cuando el bingo indicado no existe o no pertenece al organizador autenticado (AC-02/
/// AC-06) — mismo tipo para ambos casos, mismo criterio de no-enumeración que el resto del proyecto.
/// </summary>
public sealed class BingoNoEncontradoException : DomainException
{
    public BingoNoEncontradoException(string message)
        : base(message)
    {
    }
}
