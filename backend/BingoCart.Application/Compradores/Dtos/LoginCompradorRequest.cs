using System.ComponentModel.DataAnnotations;

namespace BingoCart.Application.Compradores.Dtos;

/// <summary>
/// Request de login de comprador (spec FEAT-009a, Block 2). Mismo criterio que
/// `LoginOrganizadorRequest`: las DataAnnotations van sin el target `property:` porque, en un record
/// posicional, el model binder de ASP.NET Core solo lee la metadata puesta en el parámetro del
/// constructor primario para poder bindear `[FromBody]` correctamente.
/// </summary>
public sealed record LoginCompradorRequest(
    [Required, EmailAddress] string Mail,
    [Required] string Password);
