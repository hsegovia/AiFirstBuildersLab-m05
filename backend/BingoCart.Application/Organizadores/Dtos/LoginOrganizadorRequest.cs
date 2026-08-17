using System.ComponentModel.DataAnnotations;

namespace BingoCart.Application.Organizadores.Dtos;

/// <summary>
/// Request de login de organizador (FR-01, spec FEAT-001b Block 2). Mismo criterio que
/// `RegistrarOrganizadorRequest`: las DataAnnotations van sin el target `property:` porque, en un
/// record posicional, el model binder de ASP.NET Core solo lee la metadata puesta en el parámetro
/// del constructor primario para poder bindear `[FromBody]` correctamente.
/// </summary>
public sealed record LoginOrganizadorRequest(
    [Required, EmailAddress] string Mail,
    [Required] string Password);
