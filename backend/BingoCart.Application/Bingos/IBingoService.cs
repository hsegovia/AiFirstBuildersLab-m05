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
}
