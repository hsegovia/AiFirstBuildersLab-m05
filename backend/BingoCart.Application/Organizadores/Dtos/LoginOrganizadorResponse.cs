namespace BingoCart.Application.Organizadores.Dtos;

/// <summary>
/// Response de login de organizador (spec FEAT-001b Block 2). Uso EXCLUSIVAMENTE interno: `Token`
/// viaja de Application al controller para que este arme la cookie httpOnly `bingocart_auth` — el
/// controller NUNCA devuelve este DTO ni su `Token` en el body de la respuesta HTTP. El contrato
/// público del endpoint es `LoginResponse` (`BingoCart.Api.Contracts`), que no tiene el campo
/// `Token`.
/// </summary>
public sealed record LoginOrganizadorResponse(string Token, DateTime ExpiraEnUtc);
