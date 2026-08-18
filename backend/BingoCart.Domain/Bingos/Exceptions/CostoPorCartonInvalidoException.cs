using BingoCart.Domain.Common;

namespace BingoCart.Domain.Bingos.Exceptions;

/// <summary>
/// Se lanza cuando el costo por cartón provisto no es mayor a cero (AC-06).
/// </summary>
public sealed class CostoPorCartonInvalidoException : DomainException
{
    public CostoPorCartonInvalidoException(string message)
        : base(message)
    {
    }
}
