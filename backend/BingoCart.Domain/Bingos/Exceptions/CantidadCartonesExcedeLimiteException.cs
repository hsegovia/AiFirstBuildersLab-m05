using BingoCart.Domain.Common;

namespace BingoCart.Domain.Bingos.Exceptions;

/// <summary>
/// Se lanza cuando la cantidad de cartones solicitada supera el límite máximo de 5.000 cartones
/// por bingo (RF-03, AC-02).
/// </summary>
public sealed class CantidadCartonesExcedeLimiteException : DomainException
{
    public CantidadCartonesExcedeLimiteException(string message)
        : base(message)
    {
    }
}
