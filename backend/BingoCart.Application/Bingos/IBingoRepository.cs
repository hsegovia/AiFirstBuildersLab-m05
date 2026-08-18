using BingoCart.Domain.Bingos;

namespace BingoCart.Application.Bingos;

/// <summary>
/// Puerto de persistencia de <see cref="Bingo"/>/<see cref="Carton"/> (spec FEAT-003, Block 3).
/// Application solo conoce esta firma — el detalle de EF Core vive en
/// <c>BingoCart.Infrastructure.Bingos.BingoRepository</c>.
/// </summary>
public interface IBingoRepository
{
    /// <summary>
    /// Indica si <paramref name="organizadorId"/> tiene al menos un bingo con
    /// <c>FechaSorteoUtc</c> posterior a <paramref name="ahoraUtc"/> (FR-06 — un organizador no
    /// puede tener más de un bingo vigente a la vez).
    /// </summary>
    Task<bool> TieneBingoActivoAsync(Guid organizadorId, DateTime ahoraUtc);

    /// <summary>
    /// Persiste <paramref name="bingo"/> y <paramref name="cartones"/> en una sola operación.
    /// </summary>
    Task CrearAsync(Bingo bingo, IReadOnlyList<Carton> cartones);

    /// <summary>
    /// Devuelve la página <paramref name="page"/> (1-based) de tamaño <paramref name="pageSize"/> de
    /// los bingos de <paramref name="organizadorId"/>, ordenados por <c>FechaCreacionUtc</c>
    /// descendente, junto con el total real de bingos de ese organizador (spec FEAT-004, Block 1).
    /// <paramref name="page"/>/<paramref name="pageSize"/> se asumen ya validados por el llamador
    /// (Application, Block 2) — este puerto no revalida.
    /// </summary>
    Task<BingosPaginados> ListarPorOrganizadorAsync(Guid organizadorId, int page, int pageSize);
}
