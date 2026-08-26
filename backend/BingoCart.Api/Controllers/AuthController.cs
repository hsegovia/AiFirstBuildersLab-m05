using System.Security.Claims;
using BingoCart.Api.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BingoCart.Api.Controllers;

/// <summary>
/// Expone el endpoint de resolución de rol/sesión del frontend (spec FEAT-010a, Block 1). Único
/// endpoint del controller, mínimo a propósito: sin él, el frontend no tiene forma de saber, tras
/// un refresh de página, si hay sesión activa y de qué rol es, sin depender de un dato guardado en
/// el cliente (prohibido por NFR-01 del PRD).
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    /// <summary>
    /// Devuelve el rol y el mail de la sesión activa leyendo los claims del JWT ya verificado por
    /// el pipeline de <c>AddJwtBearer</c>. <b>Excepción explícita y documentada al patrón de
    /// capas, idéntica a <see cref="OrganizadoresController.Perfil"/>:</b> no llama a ningún
    /// servicio de Application porque no hay ninguna consulta ni regla de negocio que ejecutar acá
    /// — el dato ya viene verificado por el pipeline de auth, así que delegar a Application
    /// agregaría una capa sin propósito.
    /// </summary>
    [HttpGet("whoami")]
    [Authorize]
    [EnableRateLimiting("auth-whoami")]
    [ProducesResponseType(typeof(WhoAmIResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public ActionResult<WhoAmIResponse> WhoAmI()
    {
        // [Authorize] ya garantiza un ClaimsPrincipal autenticado, y JwtTokenService siempre
        // incluye los claims Role y Email al emitir el token, para ambos roles (organizador y
        // comprador) — nunca deberían ser null acá (mismo razonamiento que
        // OrganizadoresController.Perfil sobre el claim Email).
        var rol = User.FindFirstValue(ClaimTypes.Role)!;
        var mail = User.FindFirstValue(ClaimTypes.Email)!;

        return Ok(new WhoAmIResponse(rol, mail));
    }
}
