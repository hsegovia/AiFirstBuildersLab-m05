using BingoCart.Domain.Common;

namespace BingoCart.Domain.Bingos.Exceptions;

/// <summary>
/// Se lanza cuando se intenta editar o eliminar un bingo que ya tiene compras registradas (AC-04/
/// AC-07) — un bingo con compras deja de ser modificable/eliminable.
/// </summary>
public sealed class BingoConComprasException : DomainException
{
    public BingoConComprasException(string message)
        : base(message)
    {
    }
}
