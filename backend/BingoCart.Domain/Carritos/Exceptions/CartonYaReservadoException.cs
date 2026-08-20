using BingoCart.Domain.Common;

namespace BingoCart.Domain.Carritos.Exceptions;

/// <summary>
/// Se lanza cuando el cartón que se intenta agregar existe y su bingo sigue activo, pero ya está
/// reservado por otra sesión (FR-10/AC-09) — mitad verificable hoy de "ya fue vendido" (decisión de
/// PLAN: <c>Compra</c> todavía no existe en el dominio). Vive en <c>Domain.Carritos.Exceptions</c>,
/// no en <c>Domain.Bingos.Exceptions</c>: el carrito es lo que no puede completar la operación,
/// aunque el motivo hable de un cartón.
/// </summary>
public sealed class CartonYaReservadoException : DomainException
{
    public CartonYaReservadoException(string message)
        : base(message)
    {
    }
}
