using BingoCart.Domain.Common;

namespace BingoCart.Domain.Compras.Exceptions;

/// <summary>
/// Se lanza cuando, al confirmar la compra, al menos una reserva de Redis del carrito ya no es
/// válida — expiró, o el cartón no tiene correspondencia en SQL (FR-08/AC-07/AC-08). Transporta
/// <see cref="CartonIdsInvalidos"/> como propiedad pública (no solo en el mensaje) para que Block 3
/// (Api) pueda devolverlos estructurados en el body de error 409.
/// </summary>
public sealed class ReservaCarritoInvalidaException : DomainException
{
    public IReadOnlyList<Guid> CartonIdsInvalidos { get; }

    public ReservaCarritoInvalidaException(IReadOnlyList<Guid> cartonIdsInvalidos, string message)
        : base(message)
    {
        CartonIdsInvalidos = cartonIdsInvalidos;
    }
}
