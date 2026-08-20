using BingoCart.Application.Organizadores;
using BingoCart.Domain.Compradores;

namespace BingoCart.Application.Compradores;

/// <summary>
/// Puerto (Application) hacia la persistencia de credenciales de comprador — mismo shape que
/// <c>IIdentityGateway</c> (Organizadores), decisión de PLAN: la única implementación concreta
/// (<c>IdentityGateway</c>, Infrastructure) implementa AMBAS interfaces sobre el mismo
/// <c>UserManager&lt;ApplicationUser&gt;</c>/<c>SignInManager&lt;ApplicationUser&gt;</c>, evitando
/// duplicar el wiring de Identity en dos clases de infraestructura casi idénticas.
/// </summary>
public interface ICompradorIdentityGateway
{
    Task<bool> ExisteMailAsync(string mail);

    /// <summary>
    /// Crea la cuenta de Identity para <paramref name="comprador"/> con <paramref name="password"/>
    /// y le asigna el rol <c>"Comprador"</c> (primer uso real de <c>AspNetRoles</c>/
    /// <c>AspNetUserRoles</c> del proyecto).
    /// </summary>
    Task<IdentityGatewayResult> CrearUsuarioAsync(Comprador comprador, string password);

    /// <summary>
    /// Autentica un comprador por mail y password. Misma política de lockout ya configurada para
    /// organizador (5 intentos fallidos → 5 minutos de bloqueo), sin lógica adicional de este lado.
    /// </summary>
    Task<ResultadoAutenticacion> AutenticarAsync(string mail, string password);
}
