using BingoCart.Application.Descubrimiento;
using BingoCart.Application.Descubrimiento.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BingoCart.Api.Controllers;

/// <summary>
/// Expone el descubrimiento público de cartones al azar (spec FEAT-008a, Block 2): globalmente
/// (<see cref="Descubrimiento"/>) o acotado a un organizador (<see cref="PorOrganizador"/>). Delega
/// 100% a <see cref="IDescubrimientoService"/>, sin lógica de negocio propia — mismo patrón que
/// <see cref="OrganizadoresController"/>/<see cref="BingosController"/>. Los errores los traduce
/// <see cref="Middleware.ExceptionHandlingMiddleware"/>, registrado globalmente.
/// </summary>
[AllowAnonymous]
[ApiController]
[Route("api/cartones")]
public sealed class CartonesController : ControllerBase
{
    private readonly IDescubrimientoService _descubrimientoService;

    public CartonesController(IDescubrimientoService descubrimientoService)
    {
        _descubrimientoService = descubrimientoService;
    }

    /// <summary>
    /// Hasta 5 cartones al azar entre todos los bingos vigentes de cualquier organizador (FR-01).
    /// Endpoint público: sin autenticación (<see cref="AllowAnonymousAttribute"/>, explícito de
    /// todas formas — mismo criterio documentado ya en <see cref="OrganizadoresController"/>) y con
    /// rate limiting (60 req/5 min/IP, política <c>"descubrimiento"</c> configurada en
    /// <c>Program.cs</c>).
    /// </summary>
    [HttpGet("descubrimiento")]
    [EnableRateLimiting("descubrimiento")]
    [ProducesResponseType(typeof(IReadOnlyList<CartonDescubiertoResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CartonDescubiertoResponse>>> Descubrimiento()
    {
        var response = await _descubrimientoService.DescubrirGlobalAsync();

        return Ok(response);
    }

    /// <summary>
    /// Hasta 5 cartones al azar del bingo vigente de <paramref name="organizadorId"/> (FR-05).
    /// Mismo criterio de acceso y rate limiting que <see cref="Descubrimiento"/>.
    /// </summary>
    [HttpGet("organizador/{organizadorId:guid}")]
    [EnableRateLimiting("descubrimiento")]
    [ProducesResponseType(typeof(IReadOnlyList<CartonDescubiertoResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<CartonDescubiertoResponse>>> PorOrganizador(Guid organizadorId)
    {
        var response = await _descubrimientoService.DescubrirPorOrganizadorAsync(organizadorId);

        return Ok(response);
    }
}
