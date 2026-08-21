namespace BingoCart.Application.Auth;

/// <summary>
/// Puerto de emisión de JWT — usado por organizador (FEAT-001b) y, desde FEAT-009a, por comprador.
/// Application solo conoce esta firma — el algoritmo de firma, la clave y el proveedor de tiempo son
/// detalles de <c>BingoCart.Infrastructure.Auth.JwtTokenService</c>. <paramref name="rol"/> viaja
/// como claim <c>role</c> en el JWT (spec FEAT-009a, Block 1, "Decisiones cerradas en PLAN") — mismo
/// mecanismo de firma ya auditado, sin configuración adicional en <c>Program.cs</c> para que
/// <c>[Authorize(Roles = "...")]</c> lo reconozca.
/// </summary>
public interface IJwtTokenService
{
    TokenGenerado GenerarToken(Guid userId, string mail, string rol);
}
