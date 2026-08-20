using BingoCart.Domain.Common;

namespace BingoCart.Domain.Carritos.Exceptions;

/// <summary>
/// Se lanza cuando el cartón que se intenta agregar al carrito no existe o su bingo ya no está
/// activo (<c>FechaSorteoUtc</c> vencida) — mismo tipo para ambos casos, mismo criterio de
/// no-enumeración ya usado en <c>BingoNoEncontradoException</c>. Vive en
/// <c>Domain.Carritos.Exceptions</c>, no en <c>Domain.Bingos.Exceptions</c>: el carrito es lo que no
/// puede completar la operación, aunque el motivo hable de un cartón (decisión de PLAN).
/// </summary>
public sealed class CartonInexistenteException : DomainException
{
    public CartonInexistenteException(string message)
        : base(message)
    {
    }
}
