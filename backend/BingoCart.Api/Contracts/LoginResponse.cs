namespace BingoCart.Api.Contracts;

/// <summary>
/// Contrato público de <c>POST /api/organizadores/login</c> (spec FEAT-001b, Block 2). Sin el JWT
/// (decisión de `/daw-threat-modeling` en PLAN): el token viaja exclusivamente en la cookie
/// httpOnly `bingocart_auth`, nunca en el body de la respuesta.
/// </summary>
public sealed record LoginResponse(DateTime ExpiraEnUtc);
