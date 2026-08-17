namespace BingoCart.Application.Auth;

/// <summary>
/// Resultado de <see cref="IJwtTokenService.GenerarToken"/>: el JWT y su expiración, calculados
/// juntos en un único punto (<c>BingoCart.Infrastructure.Auth.JwtTokenService</c>) para que
/// <c>ExpiraEnUtc</c> coincida exactamente con el claim <c>exp</c> real del token — evita que
/// Application recalcule la expiración con su propia llamada al reloj, que podría desincronizarse
/// por microsegundos de la usada al firmar el token (hallazgo de daw-arch-auditor en CODE, Block 2).
/// </summary>
public sealed record TokenGenerado(string Token, DateTime ExpiraEnUtc);
