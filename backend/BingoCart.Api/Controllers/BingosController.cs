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

    /// <summary>
    /// Lista paginada de los bingos propios del organizador autenticado, ordenados por fecha de
    /// creación descendente (spec FEAT-004, Block 2). Sin rate limiting (decisión cerrada en
    /// PLAN): a diferencia de <see cref="Crear"/>, este GET no tiene un costo análogo al de generar
    /// hasta 5.000 cartones — mismo criterio que <c>GET /api/organizadores/perfil</c>.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(BingoListadoResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<BingoListadoResponse>> Listar([FromQuery] ListarBingosQuery query)
    {
        // Mismo patrón que Crear: organizadorId derivado exclusivamente del claim JWT, nunca de un
        // parámetro de la request (NFR-02, mitigación R-01 del threat model).
        var organizadorId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var response = await _bingoService.ListarPropiosAsync(organizadorId, query.Page, query.PageSize);

        return Ok(response);
    }

    /// <summary>
    /// Edita nombre, fecha de sorteo y costo por cartón de un bingo propio del organizador
    /// autenticado sin compras registradas (spec FEAT-007, Block 2). Sin rate limiting (mismo
    /// criterio que <see cref="Listar"/>: no tiene un costo análogo a generar cartones).
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(BingoCreadoResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BingoCreadoResponse>> Editar(Guid id, [FromBody] EditarBingoRequest request)
    {
        // Mismo patrón que Crear/Listar: organizadorId derivado exclusivamente del claim JWT, nunca
        // del {id} de ruta ni del body (NFR-01).
        var organizadorId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var response = await _bingoService.EditarAsync(id, request, organizadorId);

        return Ok(response);
    }

    /// <summary>
    /// Elimina un bingo propio del organizador autenticado sin compras registradas, junto con
    /// todos sus cartones (spec FEAT-007, Block 2). Sin rate limiting (mismo criterio que
    /// <see cref="Editar"/>).
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Eliminar(Guid id)
    {
        // Mismo patrón que Crear/Listar/Editar: organizadorId derivado exclusivamente del claim
        // JWT, nunca del {id} de ruta (NFR-01).
        var organizadorId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        await _bingoService.EliminarAsync(id, organizadorId);

        return NoContent();
    }
}
