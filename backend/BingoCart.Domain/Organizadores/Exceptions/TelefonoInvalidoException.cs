using BingoCart.Domain.Common;

namespace BingoCart.Domain.Organizadores.Exceptions;

/// <summary>
/// Se lanza cuando el teléfono provisto no cumple el formato numérico permitido (dígitos, '+',
/// espacios y guiones) o su longitud no está entre 8 y 20 caracteres (FR-05). El mensaje nunca
/// incluye el teléfono completo: es un dato personal (NFR-02).
/// </summary>
public sealed class TelefonoInvalidoException : DomainException
{
    public TelefonoInvalidoException(string message)
        : base(message)
    {
    }
}
