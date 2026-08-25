using BingoCart.Application.Compradores.Dtos;
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

    // Métodos nuevos (spec FEAT-009d, Block 5) — actualización y lectura de los datos de cuenta.
    // `IIdentityGateway` (organizador) NO se toca: van exclusivamente acá.

    /// <summary>
    /// Devuelve el estado actual de la cuenta de <paramref name="compradorId"/>, o <c>null</c> si no
    /// existe. Usado por <c>CompradorService.ActualizarCuentaAsync</c> como snapshot previo a la
    /// escritura, para poder calcular qué campos cambiaron sin loguear ningún valor (mitigación de
    /// R-05).
    /// </summary>
    Task<CuentaCompradorResponse?> ObtenerCuentaAsync(Guid compradorId);

    /// <summary>
    /// Verifica <paramref name="password"/> contra la contraseña almacenada de
    /// <paramref name="compradorId"/>, reutilizando el mismo mecanismo de verificación que
    /// <see cref="AutenticarAsync"/> —nunca se lee ni se compara el <c>PasswordHash</c> a mano—.
    /// Es la prueba de identidad que autoriza <c>ActualizarCuentaAsync</c> (FR-13): se verifica y se
    /// descarta, nunca se persiste ni se devuelve.
    /// </summary>
    Task<bool> VerificarPasswordAsync(Guid compradorId, string password);

    /// <summary>
    /// Indica si <paramref name="mail"/> ya pertenece a una cuenta DISTINTA de
    /// <paramref name="compradorId"/> (FR-08) — reenviar el mismo mail que ya se tiene no es una
    /// colisión.
    /// </summary>
    Task<bool> ExisteMailDeOtraCuentaAsync(Guid compradorId, string mail);

    /// <summary>
    /// Indica si <paramref name="cuit"/> ya pertenece a una cuenta DISTINTA de
    /// <paramref name="compradorId"/> (FR-10) — reenviar el mismo CUIT que ya se tiene no es una
    /// colisión.
    /// </summary>
    Task<bool> ExisteCuitDeOtraCuentaAsync(Guid compradorId, string cuit);

    /// <summary>
    /// Persiste los cuatro campos de datos de cuenta juntos (A-03, actualización total, no
    /// parcial). La implementación es responsable de tocar <c>UserName</c>/
    /// <c>NormalizedUserName</c>/<c>SecurityStamp</c> además de <c>Email</c>/<c>NormalizedEmail</c>
    /// cuando el mail cambia (R-03: <c>UserManager.SetEmailAsync</c> por sí solo nunca toca
    /// <c>UserName</c>). Un <c>IdentityResult</c> no exitoso se traduce en
    /// <c>IdentityGatewayResult.Exitoso == false</c>, nunca se ignora.
    /// </summary>
    Task<IdentityGatewayResult> ActualizarDatosAsync(
        Guid compradorId, string apellido, string nombre, string cuit, string mail);
}
