using System;
using Microsoft.AspNetCore.Identity;

namespace BingoCart.Infrastructure.Identity;

/// <summary>
/// Usuario de ASP.NET Core Identity extendido con los datos de negocio del organizador
/// (Block 1 del spec FEAT-001a) y, desde FEAT-009a, del comprador — ambos roles comparten la misma
/// tabla `AspNetUsers`, distinguidos por `AspNetUserRoles` (decisión de PLAN, spec FEAT-009a). El
/// mapeo Domain (`Organizador`/`Comprador`) → `ApplicationUser` se hace en `IdentityGateway`, no
/// acá: esta clase es puro modelo de persistencia de Identity. `NombreOrganizacion`/`Telefono` son
/// nullable porque el comprador no los completa; `Apellido`/`Nombre` son nullable porque el
/// organizador no los completa — la obligatoriedad de cada campo según el rol se sigue validando en
/// `Organizador.Crear`/`Comprador.Crear` (Domain) y sus respectivos Application services, nunca a
/// nivel de columna.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public string? NombreOrganizacion { get; set; }

    public string Cuit { get; set; } = string.Empty;

    public string? Telefono { get; set; }

    public string? Apellido { get; set; }

    public string? Nombre { get; set; }
}
