using System.Security.Claims;
using BingoCart.Application.Bingos;
using BingoCart.Application.Bingos.Dtos;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BingoCart.Api.Controllers;

/// <summary>
/// Expone la creación de bingo (FR-01 a FR-07 de <c>prd-FEAT-003.md</c>). Ruta nueva
/// <c>/api/bingos</c> (no anidada bajo <c>/api/organizadores</c>, decisión cerrada en PLAN): el
/// recurso creado es un Bingo, su dueño se deriva del JWT, no de la URL. Delega 100% a
/// <see cref="IBingoService"/>, sin lógica de negocio — mismo patrón que
/// <see cref="OrganizadoresController"/>. Los errores los traduce
/// <see cref="Middleware.ExceptionHandlingMiddleware"/>, registrado globalmente.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[ApiController]
[Route("api/bingos")]
public sealed class BingosController : ControllerBase
{
    private readonly IBingoService _bingoService;

    public BingosController(IBingoService bingoService)
    {
        _bingoService = bingoService;
    }

    /// <summary>
    /// Crea un bingo para el organizador autenticado y genera atómicamente sus cartones. Rate
    /// limiting por organizador (política <c>"bingos"</c>, 3 creaciones/5 min — mitigación TM-01 de
    /// threat modeling, ver <c>Program.cs</c>).
    /// </summary>
    [HttpPost]
    [EnableRateLimiting("bingos")]
    [ProducesResponseType(typeof(BingoCreadoResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<BingoCreadoResponse>> Crear([FromBody] CrearBingoRequest request)
    {
        // [Authorize] ya garantiza un ClaimsPrincipal autenticado, y JwtTokenService siempre
        // incluye el claim NameIdentifier al emitir el token — nunca debería ser null acá (mismo
        // criterio que ClaimTypes.Email en OrganizadoresController.Perfil).
        var organizadorId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var response = await _bingoService.CrearAsync(request, organizadorId);

        return StatusCode(StatusCodes.Status201Created, response);
    }
}
