using System.Security.Cryptography;
using BingoCart.Api.Contracts;
using BingoCart.Application.Carritos;
using BingoCart.Application.Carritos.Dtos;
using BingoCart.Application.Descubrimiento.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BingoCart.Api.Controllers;

/// <summary>
/// Carrito de compras por sesión anónima (spec FEAT-008b, Block 3). No revalida nada que ya validen
/// Domain/Application: solo orquesta la llamada a <see cref="ICarritoService"/> y traduce el
/// resultado exitoso a HTTP. Los errores los traduce
/// <see cref="Middleware.ExceptionHandlingMiddleware"/>, registrado globalmente. Endpoint público
/// (<see cref="AllowAnonymousAttribute"/>, sin registro ni login) con rate limiting en los 4 métodos
/// (política <c>"carrito"</c> configurada en <c>Program.cs</c>, 60 req/5min/IP — NFR-02).
/// </summary>
[AllowAnonymous]
[ApiController]
[Route("api/carrito")]
public sealed class CarritoController : ControllerBase
{
    private const string CookieName = "bingocart_carrito";

    private readonly ICarritoService _carritoService;

    public CarritoController(ICarritoService carritoService)
    {
        _carritoService = carritoService;
    }

    /// <summary>
    /// Agrega <paramref name="cartonId"/> al carrito de la sesión actual (FR-01), creando la sesión
    /// si todavía no existía.
    /// </summary>
    [HttpPost("cartones/{cartonId:guid}")]
    [EnableRateLimiting("carrito")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Agregar(Guid cartonId)
    {
        var sesionId = ObtenerOCrearSesionId();
        await _carritoService.AgregarAsync(sesionId, cartonId);
        return NoContent();
    }

    /// <summary>
    /// Quita <paramref name="cartonId"/> del carrito de la sesión actual (FR-05). Idempotente:
    /// siempre 204, sin importar si el cartón estaba o no en el carrito.
    /// </summary>
    [HttpDelete("cartones/{cartonId:guid}")]
    [EnableRateLimiting("carrito")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Quitar(Guid cartonId)
    {
        var sesionId = ObtenerOCrearSesionId();
        await _carritoService.QuitarAsync(sesionId, cartonId);
        return NoContent();
    }

    /// <summary>
    /// Devuelve el carrito acumulado de la sesión actual con cantidad y monto total (FR-04/FR-09).
    /// Sesión nueva o sin cookie → carrito vacío, sin distinguir entre esos casos.
    /// </summary>
    [HttpGet]
    [EnableRateLimiting("carrito")]
    [ProducesResponseType(typeof(CarritoResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<CarritoResponse>> Ver()
    {
        var sesionId = ObtenerOCrearSesionId();
        return Ok(await _carritoService.ObtenerCarritoAsync(sesionId));
    }

    /// <summary>
    /// Descarta <see cref="NuevaTandaRequest.CartonIdsDescartados"/> y devuelve una nueva tanda de
    /// cartones al azar (FR-02/FR-03) — Método 1 (global) si
    /// <see cref="NuevaTandaRequest.OrganizadorId"/> es <c>null</c>, Método 2 (por organizador) si
    /// está presente, mismos dos métodos de descubrimiento de FEAT-008a.
    /// </summary>
    [HttpPost("tandas/nueva")]
    [EnableRateLimiting("carrito")]
    [ProducesResponseType(typeof(IReadOnlyList<CartonDescubiertoResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<IReadOnlyList<CartonDescubiertoResponse>>> NuevaTanda(
        [FromBody] NuevaTandaRequest request)
    {
        var sesionId = ObtenerOCrearSesionId();
        // request.CartonIdsDescartados puede llegar en null pese a ser IReadOnlyList<Guid> no-
        // nullable: la nulabilidad de C# es solo de compilación, no la respeta la deserialización
        // JSON (Hallazgo 2, revisión de Block 3) — un cliente que omite el campo simplemente no
        // excluye ningún cartón, comportamiento razonable, no un error.
        var cartonIdsDescartados = request.CartonIdsDescartados ?? Array.Empty<Guid>();
        var resultado = request.OrganizadorId is null
            ? await _carritoService.PedirNuevaTandaGlobalAsync(sesionId, cartonIdsDescartados)
            : await _carritoService.PedirNuevaTandaPorOrganizadorAsync(
                sesionId, request.OrganizadorId.Value, cartonIdsDescartados);
        return Ok(resultado);
    }

    // Fijar/leer la cookie de sesión vive acá, no en Application — mismo criterio ya documentado en
    // OrganizadoresController.Login (FEAT-001b): "fijar la cookie es un detalle de transporte HTTP,
    // no de negocio". Token opaco CSPRNG (RandomNumberGenerator.GetBytes(32), 256 bits), no
    // Guid.NewGuid() — decisión de PLAN/threat model (R-04): la posesión del token ES la
    // autorización para ese carrito, así que su entropía es la única mitigación real contra
    // adivinarlo.
    private string ObtenerOCrearSesionId()
    {
        if (Request.Cookies.TryGetValue(CookieName, out var existente) && !string.IsNullOrEmpty(existente))
        {
            return existente;
        }

        var nuevo = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        Response.Cookies.Append(CookieName, nuevo, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/"
        });
        return nuevo;
    }
}
