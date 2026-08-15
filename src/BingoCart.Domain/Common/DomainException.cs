namespace BingoCart.Domain.Common;

/// <summary>
/// Clase base abstracta de toda excepción de dominio. Permite a capas superiores (por ejemplo
/// `ExceptionHandlingMiddleware` en Block 4 de FEAT-001a) capturarlas de forma polimórfica y
/// traducirlas a una respuesta HTTP consistente, sin acoplarse a cada tipo concreto.
/// </summary>
public abstract class DomainException : Exception
{
    protected DomainException(string message)
        : base(message)
    {
    }
}
