using System.Security.Claims;
using BingoCart.Application.Compras;
using BingoCart.Application.Compras.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BingoCart.Api.Controllers;

/// <summary>
/// Expone el listado paginado de cartones adquiridos por el comprador autenticado (spec FEAT-009d,
/// Block 3). Controller PROPIO, separado de <see cref="CompradoresController"/> a propósito (D-03):
/// la mitad "cartones" del ticket no comparte archivo con la mitad "cuenta", aunque ambos exponen
/// rutas bajo el mismo prefijo <c>api/compradores</c> — ASP.NET Core enruta por atributo, no por
/// clase, así que dos controllers pueden compartir prefijo sin colisionar.
/// </summary>
[ApiController]
[Route("api/compradores")]
public sealed class MisCartonesController : ControllerBase
{
    private readonly IMisCartonesService _misCartonesService;

    public MisCartonesController(IMisCartonesService misCartonesService)
    {
        _misCartonesService = misCartonesService;
    }

    /// <summary>
    /// Lista paginada de los cartones adquiridos por el comprador autenticado (FR-11, AC-01/AC-02).
    /// <c>[Authorize(Roles = "Comprador")]</c> — <c>compradorId</c> se deriva EXCLUSIVAMENTE del
    /// claim <see cref="ClaimTypes.NameIdentifier"/> del JWT ya validado (NFR-04), nunca de un
    /// parámetro de query. Rate limiting (30 req/5min, política <c>"comprador-cuenta"</c> configurada
    /// en <c>Program.cs</c>, particionada por ese mismo claim — NFR-02): a diferencia del resto de
    /// los GET de listado de este proyecto (sin límite, ver <c>ComprasController.ListarMias</c>),
    /// este endpoint sí lo lleva por el criterio documentado en el spec de este bloque.
    /// </summary>
    [HttpGet("mis-cartones")]
    [Authorize(Roles = "Comprador")]
    [EnableRateLimiting("comprador-cuenta")]
    [ProducesResponseType(typeof(MisCartonesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<MisCartonesResponse>> Listar([FromQuery] ListarMisCartonesQuery query)
    {
        var compradorId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var response = await _misCartonesService.ListarAsync(compradorId, query.Page, query.PageSize);

        return Ok(response);
    }
}
