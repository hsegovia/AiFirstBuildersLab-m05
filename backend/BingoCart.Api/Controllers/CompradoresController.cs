using BingoCart.Application.Compradores;
using BingoCart.Application.Compradores.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BingoCart.Api.Controllers;

/// <summary>
/// Expone el registro y el login de comprador (spec FEAT-009a, Block 3) — calco exacto de
/// <see cref="OrganizadoresController"/> (misma cookie <c>bingocart_auth</c>, mismos flags
/// <c>HttpOnly</c>/<c>Secure</c>/<c>SameSite=Strict</c>, mismo criterio "fijar la cookie es
/// transporte, no negocio"). Solo dos acciones: a diferencia de organizador, el comprador no tiene
/// un endpoint de perfil ni de directorio en este ticket.
/// </summary>
[ApiController]
[Route("api/compradores")]
public sealed class CompradoresController : ControllerBase
{
    private readonly ICompradorService _compradorService;

    public CompradoresController(ICompradorService compradorService)
    {
        _compradorService = compradorService;
    }

    /// <summary>
    /// Registra un nuevo comprador y activa la cuenta inmediatamente (mismo criterio que
    /// <see cref="OrganizadoresController.RegistrarAsync"/>). Endpoint público:
    /// <see cref="AllowAnonymousAttribute"/> y rate limiting (5 req/min/IP, política
    /// <c>"compradores"</c> configurada en <c>Program.cs</c>) — este endpoint es nuevo, sin el
    /// precedente de excepción que <see cref="OrganizadoresController.Login"/> tiene hoy.
    /// </summary>
    [HttpPost("registro")]
    [AllowAnonymous]
    [EnableRateLimiting("compradores")]
    [ProducesResponseType(typeof(RegistrarCompradorResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RegistrarCompradorResponse>> RegistrarAsync(
        [FromBody] RegistrarCompradorRequest request)
    {
        var response = await _compradorService.RegistrarAsync(request);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    /// <summary>
    /// Autentica un comprador y fija el JWT emitido (con el claim <c>role: Comprador</c>) en la
    /// cookie httpOnly <c>bingocart_auth</c> — mismo mecanismo/flags que
    /// <see cref="OrganizadoresController.Login"/>, misma cookie compartida por ambos roles (el JWT
    /// distingue por su claim <c>role</c>, no la cookie por su nombre). El token NUNCA se devuelve
    /// en el body; el body de la respuesta es <c>{}</c> (spec FEAT-009a, Block 3, API contract) — a
    /// diferencia del login de organizador, que sí expone <c>expiraEnUtc</c>.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("compradores")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginCompradorRequest request)
    {
        var resultado = await _compradorService.AutenticarAsync(request);

        Response.Cookies.Append("bingocart_auth", resultado.Token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = resultado.ExpiraEnUtc,
            Path = "/"
        });

        return Ok(new { });
    }
}
