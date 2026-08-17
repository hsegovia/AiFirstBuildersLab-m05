using BingoCart.Application.Organizadores.Dtos;

namespace BingoCart.Application.Organizadores;

public interface IOrganizadorService
{
    Task<RegistrarOrganizadorResponse> RegistrarAsync(RegistrarOrganizadorRequest request);

    Task<LoginOrganizadorResponse> AutenticarAsync(LoginOrganizadorRequest request);
}
