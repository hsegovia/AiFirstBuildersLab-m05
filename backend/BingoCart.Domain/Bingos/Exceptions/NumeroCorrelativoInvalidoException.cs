using BingoCart.Domain.Common;

namespace BingoCart.Domain.Bingos.Exceptions;

/// <summary>
/// Se lanza cuando se intenta construir un <see cref="Carton"/> con un número correlativo menor a
/// 1 (FEAT-009d, FR-11). A diferencia del resto de las excepciones de dominio, esta NO se mapea en
/// `ExceptionHandlingMiddleware` y la omisión es deliberada: el correlativo siempre lo genera el
/// sistema y ningún camino de entrada de usuario alcanza ese parámetro, así que dispararla es un
/// error de programación y el 500 genérico es la respuesta correcta. Mapearla a un 4xx sugeriría
/// que el cliente puede provocarla, y no puede.
/// </summary>
public sealed class NumeroCorrelativoInvalidoException : DomainException
{
    public NumeroCorrelativoInvalidoException(string message)
        : base(message)
    {
    }
}
