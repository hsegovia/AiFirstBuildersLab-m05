using BingoCart.Domain.Common;

namespace BingoCart.Domain.Organizadores.Exceptions;

/// <summary>
/// Se lanza cuando el CUIT provisto no cumple el formato de 11 dígitos numéricos o su dígito
/// verificador no es válido según el algoritmo estándar CUIT/CUIL (FR-02). El mensaje nunca
/// incluye el CUIT completo: es un dato personal (NFR-02).
/// </summary>
public sealed class CuitInvalidoException : DomainException
{
    public CuitInvalidoException(string message)
        : base(message)
    {
    }
}
