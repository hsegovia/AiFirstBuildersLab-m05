using BingoCart.Application.Compradores.Dtos;

namespace BingoCart.Application.Compradores;

public interface ICompradorService
{
    Task<RegistrarCompradorResponse> RegistrarAsync(RegistrarCompradorRequest request);

    Task<LoginCompradorResponse> AutenticarAsync(LoginCompradorRequest request);

    /// <summary>
    /// Actualiza los datos de cuenta de <paramref name="compradorId"/> (spec FEAT-009d, Block 5/6,
    /// FR-06). No listado explícitamente entre los archivos del Block 5 del spec, pero necesario
    /// para que <c>CompradoresController</c> (Block 6) pueda depender del puerto en vez de la clase
    /// concreta — mismo patrón que <see cref="RegistrarAsync"/>/<see cref="AutenticarAsync"/>.
    /// </summary>
    Task<CuentaCompradorResponse> ActualizarCuentaAsync(Guid compradorId, ActualizarCuentaRequest request);
}
