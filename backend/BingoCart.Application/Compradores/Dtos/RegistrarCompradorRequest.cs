using System.ComponentModel.DataAnnotations;

namespace BingoCart.Application.Compradores.Dtos;

/// <summary>
/// Request de registro de comprador (spec FEAT-009a, Block 2) — mismo shape que
/// <c>RegistrarOrganizadorRequest</c>, sin <c>Telefono</c> (no lo pide el PRD). `Apellido`/`Nombre`/
/// `Mail` llevan DataAnnotations porque su validación es puramente de forma (requerido, longitud,
/// formato de mail); `Cuit` y `Password` no llevan anotaciones acá: su validación es responsabilidad
/// de Domain (`Comprador.Crear`) e Identity (`ICompradorIdentityGateway`), no de esta capa de
/// transporte.
/// </summary>
// Los atributos van sin el target `property:` a propósito, mismo criterio que
// `RegistrarOrganizadorRequest`: en un record posicional, ASP.NET Core exige que la metadata de
// validación quede en el parámetro del constructor primario para poder bindear [FromBody]
// correctamente.
public sealed record RegistrarCompradorRequest(
    [Required, StringLength(100, MinimumLength = 1)] string Apellido,
    [Required, StringLength(100, MinimumLength = 1)] string Nombre,
    string Cuit,
    [Required, EmailAddress] string Mail,
    string Password);
