using BingoCart.Domain.Common;

namespace BingoCart.Domain.Compradores.Exceptions;

/// <summary>
/// Se lanza cuando el CUIT provisto no cumple el formato de 11 dígitos numéricos o su dígito
/// verificador no es válido según el algoritmo estándar CUIT/CUIL (FR-02). El mensaje nunca incluye
/// el CUIT completo: es un dato personal (NFR-02). Mismo patrón que
/// <c>Domain.Organizadores.Exceptions.CuitInvalidoException</c>, sin reutilizarla: cada bounded
/// context tiene sus propias excepciones (decisión de PLAN).
/// </summary>
public sealed class CuitInvalidoException : DomainException
{
    public CuitInvalidoException(string message)
        : base(message)
    {
    }
}
