using BingoCart.Domain.Common;

namespace BingoCart.Domain.Bingos.Exceptions;

/// <summary>
/// Se lanza cuando la fecha de sorteo provista no es futura respecto al momento de creación del
/// bingo (AC-06).
/// </summary>
public sealed class FechaSorteoInvalidaException : DomainException
{
    public FechaSorteoInvalidaException(string message)
        : base(message)
    {
    }
}
