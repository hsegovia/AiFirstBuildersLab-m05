using System.ComponentModel.DataAnnotations;

namespace BingoCart.Application.Organizadores.Dtos;

/// <summary>
/// Request de registro de organizador (FR-01). `NombreOrganizacion` y `Mail` llevan DataAnnotations
/// porque su validación es puramente de forma (requerido, longitud, formato de mail) y se puede
/// resolver antes de tocar Domain/Identity. `Cuit`, `Telefono` y `Password` no llevan anotaciones
/// acá: su validación es responsabilidad de Domain (`Organizador.Crear`) e Identity
/// (`IIdentityGateway`), no de esta capa de transporte.
/// </summary>
// Los atributos van sin el target `property:` a propósito: en un record posicional, ASP.NET Core
// exige que la metadata de validación quede en el parámetro del constructor primario para poder
// bindear [FromBody] correctamente. `[property: ...]` la deja solo en la propiedad generada, y el
// model binder aborta con InvalidOperationException en tiempo de ejecución antes de correr el
// controller (no se detecta en tests unitarios que instancian el record directamente).
public sealed record RegistrarOrganizadorRequest(
    [Required, StringLength(200, MinimumLength = 1)] string NombreOrganizacion,
    string Cuit,
    [Required, EmailAddress] string Mail,
    string Telefono,
    string Password);
