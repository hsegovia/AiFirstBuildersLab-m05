using BingoCart.Application.Organizadores.Dtos;

namespace BingoCart.Application.Organizadores;

public interface IOrganizadorService
{
    Task<RegistrarOrganizadorResponse> RegistrarAsync(RegistrarOrganizadorRequest request);

    Task<LoginOrganizadorResponse> AutenticarAsync(LoginOrganizadorRequest request);

    /// <summary>
    /// Lista paginada del directorio público de organizadores con bingo activo (spec FEAT-005,
    /// Block 2). Sin autenticación ni <c>organizadorId</c>: expone datos de TODOS los organizadores
    /// con un bingo cuya <c>FechaSorteoUtc</c> es futura.
    /// </summary>
    Task<DirectorioResponse> ListarDirectorioAsync(int page, int pageSize);
}
