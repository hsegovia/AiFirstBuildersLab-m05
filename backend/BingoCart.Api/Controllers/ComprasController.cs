using System.Security.Claims;
using BingoCart.Application.Compras;
using BingoCart.Application.Compras.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BingoCart.Api.Controllers;

/// <summary>
/// Confirma la compra del carrito de la sesión actual (spec FEAT-009a, Block 3). No revalida nada
/// que ya validen Domain/Application: solo orquesta la llamada a <see cref="ICompraService"/> y
/// traduce el resultado exitoso a HTTP. Los errores los traduce
/// <see cref="Middleware.ExceptionHandlingMiddleware"/>, registrado globalmente.
/// </summary>
[ApiController]
[Route("api/compras")]
public sealed class ComprasController : ControllerBase
{
    private const string CookieCarritoName = "bingocart_carrito";

    private readonly ICompraService _compraService;

    public ComprasController(ICompraService compraService)
    {
        _compraService = compraService;
    }

    /// <summary>
    /// Confirma la compra (RF-14 a RF-18). <c>[Authorize(Roles = "Comprador")]</c> — mitigación R-01
    /// del threat model FEAT-009a: <paramref name="request"/> nunca lleva <c>compradorId</c>, se
    /// deriva EXCLUSIVAMENTE del claim <see cref="ClaimTypes.NameIdentifier"/> del JWT ya validado
    /// por <c>AddJwtBearer</c>, nunca de un parámetro de request. Rate limiting (10 req/5min,
    /// política <c>"compras"</c> configurada en <c>Program.cs</c>, particionada por ese mismo
    /// claim — NFR-02).
    /// </summary>
    [HttpPost("confirmar")]
    [Authorize(Roles = "Comprador")]
    [EnableRateLimiting("compras")]
    [ProducesResponseType(typeof(ConfirmarCompraResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ConfirmarCompraResponse>> Confirmar([FromBody] ConfirmarCompraRequest request)
    {
        var sesionId = ObtenerSesionIdDeCookie();
        var compradorId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var respuesta = await _compraService.ConfirmarCompraAsync(sesionId, compradorId, request.MedioPago);

        return Ok(respuesta);
    }

    // A diferencia de CarritoController.ObtenerOCrearSesionId (que SÍ crea una cookie nueva si
    // falta), acá si no hay cookie bingocart_carrito el carrito está vacío por definición: se pasa
    // sesionId vacío a ICompraService, que lo resuelve como "sin ítems" (ObtenerItemsAsync contra
    // una clave "carrito:" que nunca existe) y lanza CarritoVacioException — sin escribir ninguna
    // cookie nueva en un endpoint autenticado de escritura (decisión de PLAN, spec FEAT-009a
    // Block 3).
    private string ObtenerSesionIdDeCookie()
    {
        return Request.Cookies.TryGetValue(CookieCarritoName, out var existente) && existente is not null
            ? existente
            : string.Empty;
    }
}
