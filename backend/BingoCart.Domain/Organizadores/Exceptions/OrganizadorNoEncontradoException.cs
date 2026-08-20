using BingoCart.Domain.Common;

namespace BingoCart.Domain.Organizadores.Exceptions;

/// <summary>
/// Se lanza cuando el organizador indicado no existe (spec FEAT-008a, Block 2, Método 2 de
/// descubrimiento de cartones) — mismo patrón que <c>BingoNoEncontradoException</c>
/// (<c>Domain/Bingos/Exceptions</c>, FEAT-007): hereda <see cref="DomainException"/>, capturada
/// por un catch más en <c>ExceptionHandlingMiddleware</c> y traducida a 404. Vive en
/// <c>Domain/Organizadores/Exceptions</c> porque lo que no se encuentra es un organizador, no un
/// bingo.
/// </summary>
public sealed class OrganizadorNoEncontradoException : DomainException
{
    public OrganizadorNoEncontradoException(string message)
        : base(message)
    {
    }
}
