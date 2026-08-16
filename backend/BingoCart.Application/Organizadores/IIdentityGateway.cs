using BingoCart.Domain.Organizadores;

namespace BingoCart.Application.Organizadores;

/// <summary>
/// Puerto (Application) hacia la persistencia de credenciales. La única implementación concreta
/// (`IdentityGateway`, Infrastructure) conoce `UserManager&lt;ApplicationUser&gt;`; Application no
/// depende del tipo concreto de Identity, solo de esta abstracción.
/// </summary>
public interface IIdentityGateway
{
    Task<bool> ExisteMailAsync(string mail);

    Task<IdentityGatewayResult> CrearUsuarioAsync(Organizador organizador, string password);
}

/// <summary>
/// Resultado de intentar crear el usuario de Identity. `Errores` contiene la descripción de cada
/// regla de password incumplida (nunca la contraseña en sí) cuando `Exitoso` es `false`.
/// </summary>
public sealed record IdentityGatewayResult(bool Exitoso, IReadOnlyList<string> Errores);
