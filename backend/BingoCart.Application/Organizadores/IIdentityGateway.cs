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

    /// <summary>
    /// Autentica un organizador por mail y password (spec FEAT-001b, Block 2). Aplica la política
    /// de lockout configurada en Identity (5 intentos fallidos → 5 minutos de bloqueo) sin lógica
    /// adicional de este lado: la única implementación concreta delega en
    /// <c>SignInManager&lt;ApplicationUser&gt;.CheckPasswordSignInAsync</c>.
    /// </summary>
    Task<ResultadoAutenticacion> AutenticarAsync(string mail, string password);
}

/// <summary>
/// Resultado de intentar crear el usuario de Identity. `Errores` contiene la descripción de cada
/// regla de password incumplida (nunca la contraseña en sí) cuando `Exitoso` es `false`.
/// </summary>
public sealed record IdentityGatewayResult(bool Exitoso, IReadOnlyList<string> Errores);
