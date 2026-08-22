using BingoCart.Domain.Common;

namespace BingoCart.Domain.Compras.Exceptions;

/// <summary>
/// Se lanza cuando la compra indicada no existe o no pertenece al organizador autenticado (FR-08/
/// AC-08) — mismo tipo para ambos casos, mismo criterio de no-enumeración que el resto del proyecto.
/// </summary>
public sealed class CompraNoEncontradaException : DomainException
{
    public CompraNoEncontradaException(string message)
        : base(message)
    {
    }
}
