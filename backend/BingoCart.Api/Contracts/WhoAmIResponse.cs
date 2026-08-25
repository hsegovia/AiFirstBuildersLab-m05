namespace BingoCart.Api.Contracts;

/// <summary>
/// Contrato público de <c>GET /api/auth/whoami</c> (spec FEAT-010a, Block 1). Vive en
/// <c>Api/Contracts/</c> y no en <c>Application/</c> porque ningún servicio de Application
/// construye ni toca este DTO: el rol y el mail salen directo de los claims del JWT ya verificado
/// por el middleware de <c>AddJwtBearer</c> (mismo patrón que <c>PerfilOrganizadorResponse.cs</c>).
/// <b>Mitigación de Information Disclosure HIGH (R-01 del threat model FEAT-010a):</b> este record
/// declara EXACTAMENTE estas 2 propiedades, nunca más — nunca se serializa <c>User.Claims</c>
/// genéricamente ni se agrega <c>NameIdentifier</c> ni el JWT.
/// </summary>
public sealed record WhoAmIResponse(string Rol, string Mail);
