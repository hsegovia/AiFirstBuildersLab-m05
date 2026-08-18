using BingoCart.Domain.Common;

namespace BingoCart.Domain.Bingos.Exceptions;

/// <summary>
/// Se lanza cuando la cantidad de cartones solicitada no es mayor a cero (AC-06).
/// </summary>
public sealed class CantidadCartonesInvalidaException : DomainException
{
    public CantidadCartonesInvalidaException(string message)
        : base(message)
    {
    }
}
