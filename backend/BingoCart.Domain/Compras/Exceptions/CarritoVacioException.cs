using BingoCart.Domain.Common;

namespace BingoCart.Domain.Compras.Exceptions;

/// <summary>
/// Se lanza cuando se intenta confirmar la compra de un carrito sin ningún ítem (FR-04/AC-02). La
/// lanza <c>CompraService</c> (Application, Block 2) antes de consultar Redis/SQL — Domain no
/// resuelve el carrito por sí mismo.
/// </summary>
public sealed class CarritoVacioException : DomainException
{
    public CarritoVacioException(string message)
        : base(message)
    {
    }
}
