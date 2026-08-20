using BingoCart.Application.Compradores.Dtos;

namespace BingoCart.Application.Compradores;

public interface ICompradorService
{
    Task<RegistrarCompradorResponse> RegistrarAsync(RegistrarCompradorRequest request);

    Task<LoginCompradorResponse> AutenticarAsync(LoginCompradorRequest request);
}
