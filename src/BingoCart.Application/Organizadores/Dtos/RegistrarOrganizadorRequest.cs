using System.ComponentModel.DataAnnotations;

namespace BingoCart.Application.Organizadores.Dtos;

/// <summary>
/// Request de registro de organizador (FR-01). `NombreOrganizacion` y `Mail` llevan DataAnnotations
/// porque su validación es puramente de forma (requerido, longitud, formato de mail) y se puede
/// resolver antes de tocar Domain/Identity. `Cuit`, `Telefono` y `Password` no llevan anotaciones
/// acá: su validación es responsabilidad de Domain (`Organizador.Crear`) e Identity
/// (`IIdentityGateway`), no de esta capa de transporte.
/// </summary>
public sealed record RegistrarOrganizadorRequest(
    [property: Required, StringLength(200, MinimumLength = 1)] string NombreOrganizacion,
    string Cuit,
    [property: Required, EmailAddress] string Mail,
    string Telefono,
    string Password);
