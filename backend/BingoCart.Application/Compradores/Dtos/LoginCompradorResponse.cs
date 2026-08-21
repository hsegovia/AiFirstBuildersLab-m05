namespace BingoCart.Application.Compradores.Dtos;

/// <summary>
/// Response de login de comprador (spec FEAT-009a, Block 2). Uso EXCLUSIVAMENTE interno: `Token`
/// viaja de Application al controller para que este arme la cookie httpOnly `bingocart_auth` — el
/// controller (Block 3) NUNCA devuelve este DTO ni su `Token` en el body de la respuesta HTTP, mismo
/// criterio que `LoginOrganizadorResponse`.
/// </summary>
public sealed record LoginCompradorResponse(string Token, DateTime ExpiraEnUtc);
