using BingoCart.Domain.Common;

namespace BingoCart.Domain.Auth.Exceptions;

/// <summary>
/// Se lanza cuando el login de un organizador falla, ya sea por mail inexistente, password
/// incorrecta o cuenta bloqueada por lockout (spec FEAT-001b, Block 2). Mensaje genérico y ÚNICO
/// para los tres casos, a propósito: un 401 nunca debe confirmar por sí solo si la cuenta existe o
/// está bloqueada (AC-02/AC-03). Vive en `Domain/Auth/` (no `Domain/Organizadores/`) porque
/// "credenciales inválidas" es un resultado de autenticación, no un invariante del agregado
/// `Organizador` — espeja la separación que Application ya hace entre `Application/Auth/` (JWT) y
/// `Application/Organizadores/` (registro/perfil).
/// </summary>
public sealed class CredencialesInvalidasException : DomainException
{
    public CredencialesInvalidasException()
        : base("Credenciales inválidas.")
    {
    }
}
