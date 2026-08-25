using BingoCart.Domain.Common;

namespace BingoCart.Domain.Compradores.Exceptions;

/// <summary>
/// Se lanza cuando el CUIT enviado en una actualización de cuenta (FR-10) ya pertenece a otra
/// cuenta distinta de la que está actualizando sus datos. El mensaje NUNCA repite el CUIT enviado:
/// un CUIT identifica unívocamente a una persona real, así que este caso es el más sensible de los
/// dos análogos de colisión (mitigación de R-04 del threat model).
/// </summary>
public sealed class CuitEnUsoException : DomainException
{
    public CuitEnUsoException(string message)
        : base(message)
    {
    }
}
