using System.Security.Claims;
using BingoCart.Api.Contracts;
using BingoCart.Application.Organizadores;
using BingoCart.Application.Organizadores.Dtos;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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

    /// <summary>
    /// Autentica un organizador (spec FEAT-001b, Block 2) y fija el JWT emitido en una cookie
    /// httpOnly (`bingocart_auth`) — decisión de `/daw-threat-modeling` en PLAN para eliminar el
    /// vector de robo del token vía XSS. Es la ÚNICA lógica que vive en el controller en vez de en
    /// Application: fijar la cookie es un detalle de transporte HTTP, no de negocio. El token NUNCA
    /// se devuelve en el body de la respuesta.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginOrganizadorRequest request)
    {
        var resultado = await _organizadorService.AutenticarAsync(request);

        Response.Cookies.Append("bingocart_auth", resultado.Token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = resultado.ExpiraEnUtc,
            Path = "/"
        });

        return Ok(new LoginResponse(resultado.ExpiraEnUtc));
    }

    /// <summary>
    /// Endpoint mínimo de verificación (spec FEAT-001b, Block 3): su único propósito es hacer
    /// comprobable, con una request HTTP real, que el pipeline de <c>AddJwtBearer</c> (Block 1)
    /// rechaza tokens inválidos/expirados y acepta los válidos, dado que ningún endpoint de
    /// negocio protegido existe todavía en este ticket.
    /// <b>Excepción explícita y documentada al patrón de capas:</b> no llama a ningún servicio de
    /// <see cref="IOrganizadorService"/>. El claim ya viene verificado por el middleware de
    /// <c>AddJwtBearer</c> — no hay ninguna consulta ni regla de negocio que ejecutar acá, así que
    /// delegar a Application agregaría una capa sin propósito para un endpoint cuyo único fin es
    /// verificar la infraestructura de autenticación, no exponer un caso de uso de negocio.
    /// <b>Nota de implementación:</b> el conflicto entre el cookie scheme de Identity y JwtBearer
    /// (ver <c>Program.cs</c> para la causa raíz y la corrección central) ya está resuelto a nivel
    /// global. El <c>AuthenticationSchemes</c> explícito de abajo no es estrictamente necesario tras
    /// esa corrección — se deja de todos modos como documentación del contrato de este endpoint
    /// (defensa en profundidad: exige JWT explícitamente aunque el default global cambie el día de
    /// mañana).
    /// </summary>
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpGet("perfil")]
    [ProducesResponseType(typeof(PerfilOrganizadorResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<PerfilOrganizadorResponse> Perfil()
    {
        // [Authorize] ya garantiza un ClaimsPrincipal autenticado, y JwtTokenService (Block 1)
        // siempre incluye el claim Email al emitir el token — nunca debería ser null acá.
        var mail = User.FindFirstValue(ClaimTypes.Email)!;
        return Ok(new PerfilOrganizadorResponse(mail));
    }

    /// <summary>
    /// Directorio público de organizadores con un bingo cuyo sorteo todavía no ocurrió (spec
    /// FEAT-005, Block 2). Endpoint público: sin autenticación (<see cref="AllowAnonymousAttribute"/>,
    /// mismo patrón que <see cref="RegistrarAsync"/>/<see cref="Login"/>) y con rate limiting
    /// (30 req/5 min/IP, política <c>"directorio"</c> configurada en <c>Program.cs</c>) para mitigar
    /// spam/DoS (threat model FEAT-005, riesgo R-02). Delega 100% a
    /// <see cref="IOrganizadorService.ListarDirectorioAsync"/>, sin lógica de negocio propia.
    /// </summary>
    [HttpGet("directorio")]
    [AllowAnonymous]
    [EnableRateLimiting("directorio")]
    [ProducesResponseType(typeof(DirectorioResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DirectorioResponse>> Directorio([FromQuery] ListarDirectorioQuery query)
    {
        var response = await _organizadorService.ListarDirectorioAsync(query.Page, query.PageSize);

        return Ok(response);
    }
}
