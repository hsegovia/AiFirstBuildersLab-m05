using BingoCart.Application.Auth;
using BingoCart.Application.Compradores.Dtos;
using BingoCart.Application.Organizadores;
using BingoCart.Domain.Auth.Exceptions;
using BingoCart.Domain.Compradores;
using BingoCart.Domain.Compradores.Exceptions;

namespace BingoCart.Application.Compradores;

/// <summary>
/// Orquesta el registro y el login de comprador (spec FEAT-009a, Block 2) — calco exacto de
/// <see cref="Organizadores.OrganizadorService.RegistrarAsync"/>/
/// <see cref="Organizadores.OrganizadorService.AutenticarAsync"/> (mismo orden: Domain valida →
/// unicidad de mail → gateway → JWT con `rol: "Comprador"`). No hace I/O propio: toda la
/// persistencia/verificación de credenciales pasa por <see cref="ICompradorIdentityGateway"/>,
/// inyectado.
/// </summary>
public sealed class CompradorService : ICompradorService
{
    private const string Rol = "Comprador";

    private readonly ICompradorIdentityGateway _gateway;
    private readonly IJwtTokenService _jwtTokenService;

    public CompradorService(ICompradorIdentityGateway gateway, IJwtTokenService jwtTokenService)
    {
        _gateway = gateway;
        _jwtTokenService = jwtTokenService;
    }

    public async Task<RegistrarCompradorResponse> RegistrarAsync(RegistrarCompradorRequest request)
    {
        // (1) Domain valida CUIT y lanza CuitInvalidoException sin haber llamado todavía al gateway.
        var comprador = Comprador.Crear(request.Apellido, request.Nombre, request.Cuit, request.Mail);

        // (2) Unicidad de mail: si ya existe, no se llega a intentar crear el usuario de Identity.
        var mailYaExiste = await _gateway.ExisteMailAsync(comprador.Mail);
        if (mailYaExiste)
        {
            throw new MailYaRegistradoException(
                "El mail ingresado ya pertenece a una cuenta existente.");
        }

        // (3) Política de password delegada íntegramente a Identity vía el gateway.
        var resultado = await _gateway.CrearUsuarioAsync(comprador, request.Password);
        if (!resultado.Exitoso)
        {
            throw new PasswordInvalidaException(resultado.Errores);
        }

        return new RegistrarCompradorResponse(comprador.Id, comprador.Apellido, comprador.Nombre, comprador.Mail);
    }

    public async Task<LoginCompradorResponse> AutenticarAsync(LoginCompradorRequest request)
    {
        var resultado = await _gateway.AutenticarAsync(request.Mail, request.Password);

        if (resultado.Estado != EstadoAutenticacion.Exitoso)
        {
            // Mismo tipo y mismo mensaje para CredencialesInvalidas y CuentaBloqueada (mismo
            // criterio que OrganizadorService): un 401 nunca debe confirmar por sí solo que la
            // cuenta existe o está bloqueada.
            throw new CredencialesInvalidasException();
        }

        // Invariante de ICompradorIdentityGateway.AutenticarAsync: Estado == Exitoso siempre viene
        // con OrganizadorId poblado (mismo campo reutilizado de ResultadoAutenticacion, aquí
        // representa el id del comprador). Se refuerza explícitamente acá en vez de un `!.Value`.
        if (resultado.OrganizadorId is not { } compradorId)
        {
            throw new InvalidOperationException(
                "ICompradorIdentityGateway.AutenticarAsync devolvió Exitoso sin OrganizadorId.");
        }

        var tokenGenerado = _jwtTokenService.GenerarToken(compradorId, request.Mail, Rol);

        return new LoginCompradorResponse(tokenGenerado.Token, tokenGenerado.ExpiraEnUtc);
    }
}
