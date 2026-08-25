using BingoCart.Domain.Common;

namespace BingoCart.Domain.Compradores.Exceptions;

/// <summary>
/// Se lanza cuando el mail enviado en una actualización de cuenta (FR-08) ya pertenece a otra
/// cuenta distinta de la que está actualizando sus datos. El mensaje NUNCA repite la dirección
/// enviada: hacerlo dejaría PII en la respuesta HTTP y en cualquier log que la capture (mitigación
/// de R-04 del threat model). Distinta de <c>MailYaRegistradoException</c> (que se lanza durante el
/// registro, no la actualización) a propósito: son dos operaciones distintas con su propio código de
/// error en el API contract.
/// </summary>
public sealed class MailEnUsoException : DomainException
{
    public MailEnUsoException(string message)
        : base(message)
    {
    }
}
