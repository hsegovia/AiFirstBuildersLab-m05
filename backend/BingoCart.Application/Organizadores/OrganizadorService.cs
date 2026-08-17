using BingoCart.Application.Auth;
using BingoCart.Application.Organizadores.Dtos;
using BingoCart.Domain.Auth.Exceptions;
using BingoCart.Domain.Organizadores;
using BingoCart.Domain.Organizadores.Exceptions;

namespace BingoCart.Application.Organizadores;

/// <summary>
/// Orquesta el registro (FR-01, FR-03, FR-04, FR-06, FR-07) y el login (spec FEAT-001b, Block 2) de
/// organizador. No hace I/O propio: toda la persistencia/verificación de credenciales pasa por
/// <see cref="IIdentityGateway"/>, inyectado.
/// </summary>
public sealed class OrganizadorService : IOrganizadorService
{
    private readonly IIdentityGateway _gateway;
    private readonly IJwtTokenService _jwtTokenService;

    public OrganizadorService(IIdentityGateway gateway, IJwtTokenService jwtTokenService)
    {
        _gateway = gateway;
        _jwtTokenService = jwtTokenService;
    }

    public async Task<RegistrarOrganizadorResponse> RegistrarAsync(RegistrarOrganizadorRequest request)
    {
        // (1) Domain valida CUIT/teléfono y lanza CuitInvalidoException/TelefonoInvalidoException
        // sin haber llamado todavía al gateway.
        var organizador = Organizador.Crear(
            request.NombreOrganizacion,
            request.Cuit,
            request.Mail,
            request.Telefono);

        // (2) Unicidad de mail: si ya existe, no se llega a intentar crear el usuario de Identity.
        var mailYaExiste = await _gateway.ExisteMailAsync(organizador.Mail);
        if (mailYaExiste)
        {
            throw new MailYaRegistradoException(
                "El mail ingresado ya pertenece a una cuenta de organizador existente.");
        }

        // (3) Política de password delegada íntegramente a Identity vía el gateway.
        var resultado = await _gateway.CrearUsuarioAsync(organizador, request.Password);
        if (!resultado.Exitoso)
        {
            throw new PasswordInvalidaException(resultado.Errores);
        }

        return new RegistrarOrganizadorResponse(organizador.Id, organizador.NombreOrganizacion, organizador.Mail);
    }

    public async Task<LoginOrganizadorResponse> AutenticarAsync(LoginOrganizadorRequest request)
    {
        var resultado = await _gateway.AutenticarAsync(request.Mail, request.Password);

        if (resultado.Estado != EstadoAutenticacion.Exitoso)
        {
            // Mismo tipo y mismo mensaje para CredencialesInvalidas y CuentaBloqueada (AC-02/AC-03):
            // un 401 nunca debe confirmar por sí solo que la cuenta existe o está bloqueada.
            throw new CredencialesInvalidasException();
        }

        // Invariante de IIdentityGateway.AutenticarAsync: Estado == Exitoso siempre viene con
        // OrganizadorId poblado. Se refuerza explícitamente acá (en vez de un `!.Value`) para que
        // una implementación futura que rompa el invariante falle con un mensaje claro en este
        // punto, no con un NullReferenceException aguas abajo.
        if (resultado.OrganizadorId is not { } organizadorId)
        {
            throw new InvalidOperationException(
                "IIdentityGateway.AutenticarAsync devolvió Exitoso sin OrganizadorId.");
        }

        var tokenGenerado = _jwtTokenService.GenerarToken(organizadorId, request.Mail);

        return new LoginOrganizadorResponse(tokenGenerado.Token, tokenGenerado.ExpiraEnUtc);
    }
}
