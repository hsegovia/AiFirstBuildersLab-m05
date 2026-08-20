using BingoCart.Application.Bingos.Dtos;

namespace BingoCart.Application.Bingos;

/// <summary>
/// Puerto de orquestación de creación de bingo (spec FEAT-003, Block 4). El controller (Api) solo
/// conoce esta firma — la orquestación real (dominio + generador CSPRNG + repositorio) vive en
/// <c>BingoService</c>.
/// </summary>
public interface IBingoService
{
    /// <summary>
    /// Crea un bingo para <paramref name="organizadorId"/> y genera atómicamente sus cartones.
    /// </summary>
    Task<BingoCreadoResponse> CrearAsync(CrearBingoRequest request, Guid organizadorId);

    /// <summary>
    /// Lista paginada de los bingos propios de <paramref name="organizadorId"/>, ordenados por
    /// fecha de creación descendente (spec FEAT-004, Block 2). <paramref name="pageSize"/> se
    /// clampea a 100 antes de llegar a <see cref="IBingoRepository"/> (AC-06, defensa en
    /// profundidad ante R-02 del threat model) — el <c>PageSize</c> devuelto en la respuesta es el
    /// valor ya clampeado, no el solicitado.
    /// </summary>
    Task<BingoListadoResponse> ListarPropiosAsync(Guid organizadorId, int page, int pageSize);

    /// <summary>
    /// Edita nombre, fecha de sorteo y costo por cartón de un bingo propio de
    /// <paramref name="organizadorId"/> sin compras registradas (spec FEAT-007, Block 2). Lanza
    /// <see cref="BingoCart.Domain.Bingos.Exceptions.BingoNoEncontradoException"/> si el bingo no
    /// existe o pertenece a otro organizador (mismo tipo para ambos casos, no-enumeración), y
    /// <see cref="BingoCart.Domain.Bingos.Exceptions.BingoConComprasException"/> si ya tiene
    /// compras registradas.
    /// </summary>
    Task<BingoCreadoResponse> EditarAsync(Guid id, EditarBingoRequest request, Guid organizadorId);

    /// <summary>
    /// Elimina un bingo propio de <paramref name="organizadorId"/> sin compras registradas, junto
    /// con todos sus cartones (spec FEAT-007, Block 2). Mismas excepciones que
    /// <see cref="EditarAsync"/>.
    /// </summary>
    Task EliminarAsync(Guid id, Guid organizadorId);
}
