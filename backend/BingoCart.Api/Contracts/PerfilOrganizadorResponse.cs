namespace BingoCart.Api.Contracts;

/// <summary>
/// Contrato público de <c>GET /api/organizadores/perfil</c> (spec FEAT-001b, Block 3). Vive en
/// <c>Api/Contracts/</c> y no en <c>Application/Organizadores/Dtos/</c> porque ningún servicio de
/// Application construye ni toca este DTO: el mail sale directo del claim del JWT ya verificado
/// por el middleware de <c>AddJwtBearer</c> (Block 1).
/// </summary>
public sealed record PerfilOrganizadorResponse(string Mail);
