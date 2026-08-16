using BingoCart.Application.Organizadores;
using BingoCart.Application.Organizadores.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BingoCart.Api.Controllers;

/// <summary>
/// Expone el registro de organizador (FR-01 a FR-07 de <c>prd-FEAT-001a.md</c>). No revalida nada
/// que ya validen Domain/Application: solo orquesta la llamada a <see cref="IOrganizadorService"/>
/// y traduce el resultado exitoso a HTTP. Los errores los traduce
/// <see cref="Middleware.ExceptionHandlingMiddleware"/>, registrado globalmente. Nunca serializa
/// <c>Organizador</c> ni <c>ApplicationUser</c> directamente: solo el DTO de Application.
/// </summary>
[ApiController]
[Route("api/organizadores")]
public sealed class OrganizadoresController : ControllerBase
{
    private readonly IOrganizadorService _organizadorService;
    private readonly ILogger<OrganizadoresController> _logger;

    public OrganizadoresController(IOrganizadorService organizadorService, ILogger<OrganizadoresController> logger)
    {
        _organizadorService = organizadorService;
        _logger = logger;
    }

    /// <summary>
    /// Registra un nuevo organizador y activa la cuenta inmediatamente (FR-06). Endpoint público:
    /// sin autenticación (<see cref="AllowAnonymousAttribute"/>) y con rate limiting (5 req/min/IP,
    /// política <c>"registro"</c> configurada en <c>Program.cs</c>) para mitigar spam/DoS
    /// (threat model, riesgo #3).
    /// </summary>
    [HttpPost("registro")]
    [AllowAnonymous]
    [EnableRateLimiting("registro")]
    [ProducesResponseType(typeof(RegistrarOrganizadorResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<RegistrarOrganizadorResponse>> RegistrarAsync(
        [FromBody] RegistrarOrganizadorRequest request)
    {
        var response = await _organizadorService.RegistrarAsync(request);

        // Auditoría de registro exitoso (mitigación de repudio, threat model riesgo #5): SOLO el
        // Guid generado, nunca CUIT/mail/teléfono/nombre de organización (NFR-02).
        _logger.LogInformation("Organizador registrado: {Id}", response.Id);

        return StatusCode(StatusCodes.Status201Created, response);
    }
}
