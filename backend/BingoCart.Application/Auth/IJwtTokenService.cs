namespace BingoCart.Application.Auth;

/// <summary>
/// Puerto de emisión de JWT para organizadores autenticados. Application solo conoce esta firma —
/// el algoritmo de firma, la clave y el proveedor de tiempo son detalles de
/// <c>BingoCart.Infrastructure.Auth.JwtTokenService</c> (spec FEAT-001b, Block 1).
/// </summary>
public interface IJwtTokenService
{
    TokenGenerado GenerarToken(Guid organizadorId, string mail);
}
