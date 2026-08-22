using System.ComponentModel.DataAnnotations;

namespace BingoCart.Application.Compras.Dtos;

/// <summary>
/// Query de listado paginado de compras propias del organizador (spec FEAT-009c, Block 2) — mismo
/// patrón exacto que <see cref="BingoCart.Application.Bingos.Dtos.ListarBingosQuery"/>.
/// <c>PageSize</c> &gt; 50 NO se rechaza acá — se clampea en
/// <c>CompraOrganizadorService.ListarPropiasAsync</c> (NFR-01, defensa en profundidad).
/// </summary>
public sealed record ListarComprasQuery(
    [Range(1, int.MaxValue)] int Page = 1,
    [Range(1, int.MaxValue)] int PageSize = 20);
