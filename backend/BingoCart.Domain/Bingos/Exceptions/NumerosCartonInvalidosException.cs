using BingoCart.Domain.Common;

namespace BingoCart.Domain.Bingos.Exceptions;

/// <summary>
/// Se lanza cuando el conjunto de números provisto para un cartón no tiene exactamente 10 números
/// distintos entre sí — invariante propia del cartón, independiente de la unicidad entre cartones
/// del mismo bingo (que valida Block 2).
/// </summary>
public sealed class NumerosCartonInvalidosException : DomainException
{
    public NumerosCartonInvalidosException(string message)
        : base(message)
    {
    }
}
