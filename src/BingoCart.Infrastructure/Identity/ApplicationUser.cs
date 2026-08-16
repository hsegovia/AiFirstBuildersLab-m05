using System;
using Microsoft.AspNetCore.Identity;

namespace BingoCart.Infrastructure.Identity;

/// <summary>
/// Usuario de ASP.NET Core Identity extendido con los datos de negocio del organizador
/// (Block 1 del spec FEAT-001a). El mapeo Domain (`Organizador`) → `ApplicationUser` se hace en
/// `IdentityGateway` (Block 3), no acá: esta clase es puro modelo de persistencia de Identity.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public string NombreOrganizacion { get; set; } = string.Empty;

    public string Cuit { get; set; } = string.Empty;

    public string Telefono { get; set; } = string.Empty;
}
