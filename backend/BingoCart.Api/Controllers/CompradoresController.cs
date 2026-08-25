using System.Security.Claims;
using BingoCart.Application.Auth;
using BingoCart.Application.Compradores;
using BingoCart.Application.Compradores.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BingoCart.Api.Controllers;

/// <summary>
/// Expone el registro, el login y la actualización de datos de cuenta de comprador (spec
/// FEAT-009a, Block 3; spec FEAT-009d, Block 6) — calco exacto de
/// <see cref="OrganizadoresController"/> (misma cookie <c>bingocart_auth</c>, mismos flags
/// <c>HttpOnly</c>/<c>Secure</c>/<c>SameSite=Strict</c>, mismo criterio "fijar la cookie es
/// transporte, no negocio"). A diferencia de organizador, el comprador no tiene un endpoint de
/// perfil (GET) ni de directorio en este ticket (A-05): el `PUT` de abajo devuelve el estado ya
/// actualizado, así que un GET separado no agrega nada.
/// </summary>
[ApiController]
[Route("api/compradores")]
public sealed class CompradoresController : ControllerBase
{
    private readonly ICompradorService _compradorService;
    private readonly IJwtTokenService _jwtTokenService;

    public CompradoresController(ICompradorService compradorService, IJwtTokenService jwtTokenService)
    {
        _compradorService = compradorService;
        _jwtTokenService = jwtTokenService;
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

    /// <summary>
    /// Actualiza los datos de cuenta del comprador autenticado (spec FEAT-009d, Block 6, FR-06).
    /// <c>compradorId</c> se deriva EXCLUSIVAMENTE del claim <see cref="ClaimTypes.NameIdentifier"/>
    /// del JWT ya validado (NFR-04) — el body no lleva ningún identificador de usuario. No existe un
    /// <c>GET</c> de perfil separado (ver doc-comment de la clase, A-05): este <c>PUT</c> devuelve el
    /// estado ya actualizado y eso alcanza para AC-06.
    ///
    /// Tras un éxito se reemite la cookie <c>bingocart_auth</c> con un JWT nuevo (mismo mecanismo que
    /// <see cref="Login"/>: <see cref="IJwtTokenService.GenerarToken"/> + los mismos flags
    /// HttpOnly/Secure/SameSite=Strict), porque el claim <c>Email</c> del token vigente quedaría con
    /// el mail viejo hasta que expire (D-05). Esto es puramente cosmético: el proyecto autentica con
    /// JWT Bearer stateless y no tiene <c>SecurityStampValidator</c> en el pipeline, así que reemitir
    /// esta cookie NO revoca ningún token ya emitido en otro dispositivo — esos siguen siendo válidos
    /// hasta que expiren solos.
    /// </summary>
    [HttpPut("mi-cuenta")]
    [Authorize(Roles = "Comprador")]
    [EnableRateLimiting("comprador-cuenta")]
    [ProducesResponseType(typeof(CuentaCompradorResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<CuentaCompradorResponse>> ActualizarCuentaAsync(
        [FromBody] ActualizarCuentaRequest request)
    {
        var compradorId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var response = await _compradorService.ActualizarCuentaAsync(compradorId, request);

        var tokenGenerado = _jwtTokenService.GenerarToken(compradorId, response.Mail, "Comprador");
        Response.Cookies.Append("bingocart_auth", tokenGenerado.Token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = tokenGenerado.ExpiraEnUtc,
            Path = "/"
        });

        return Ok(response);
    }
}
