using BingoCart.Application.Organizadores.Dtos;
using BingoCart.Domain.Organizadores;
using BingoCart.Domain.Organizadores.Exceptions;

namespace BingoCart.Application.Organizadores;

/// <summary>
/// Orquesta el registro de organizador (FR-01, FR-03, FR-04, FR-06, FR-07). No hace I/O propio:
/// toda la persistencia de credenciales pasa por <see cref="IIdentityGateway"/>, inyectado.
/// </summary>
public sealed class OrganizadorService : IOrganizadorService
{
    private readonly IIdentityGateway _gateway;

    public OrganizadorService(IIdentityGateway gateway)
    {
        _gateway = gateway;
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
}
