namespace BingoCart.Application.Compradores.Dtos;

/// <summary>
/// Response de registro de comprador. Nunca expone `Comprador` ni `ApplicationUser` directamente
/// (convención de capas: Application/Api solo hablan DTOs).
/// </summary>
public sealed record RegistrarCompradorResponse(Guid Id, string Apellido, string Nombre, string Mail);
