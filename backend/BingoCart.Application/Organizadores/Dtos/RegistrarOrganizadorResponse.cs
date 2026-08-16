namespace BingoCart.Application.Organizadores.Dtos;

/// <summary>
/// Response de registro de organizador. Nunca expone `Organizador` ni `ApplicationUser`
/// directamente (convención de capas: Application/Api solo hablan DTOs).
/// </summary>
public sealed record RegistrarOrganizadorResponse(Guid Id, string NombreOrganizacion, string Mail);
