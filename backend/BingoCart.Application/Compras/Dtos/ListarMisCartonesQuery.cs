using System.ComponentModel.DataAnnotations;

namespace BingoCart.Application.Compras.Dtos;

/// <summary>
/// Query de listado paginado de "mis cartones" del comprador autenticado (spec FEAT-009d, Block 3)
/// — mismo `sealed record` posicional exacto que <see cref="ListarComprasQuery"/>. <c>PageSize</c>
/// &gt; 50 NO se rechaza acá — se clampea en <c>MisCartonesService.ListarAsync</c> (NFR-01, defensa
/// en profundidad).
/// </summary>
public sealed record ListarMisCartonesQuery(
    [Range(1, int.MaxValue)] int Page = 1,
    [Range(1, int.MaxValue)] int PageSize = 20);
