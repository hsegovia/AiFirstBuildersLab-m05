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

    /// <summary>
    /// Devuelve el <see cref="Bingo"/> con <paramref name="id"/>, trackeado por el <c>DbContext</c>
    /// (spec FEAT-007, Block 1) — así una mutación posterior vía <c>Bingo.Actualizar</c> queda
    /// detectada por EF Core sin necesidad de un <c>Update()</c> explícito. <c>null</c> si no existe.
    /// </summary>
    Task<Bingo?> ObtenerPorIdAsync(Guid id);

    /// <summary>
    /// Elimina <paramref name="bingo"/> junto con todos sus <c>Cartones</c> (cascade configurado a
    /// nivel de esquema en <c>AppDbContext</c>) en una sola operación.
    /// </summary>
    Task EliminarAsync(Bingo bingo);

    /// <summary>
    /// Indica si el bingo <paramref name="bingoId"/> tiene al menos una compra registrada. Punto de
    /// extensión: hoy siempre devuelve <c>false</c> porque la entidad <c>Compra</c> todavía no existe
    /// (ticket futuro de carrito/compra) — ver implementación en
    /// <c>BingoCart.Infrastructure.Bingos.BingoRepository</c>.
    /// </summary>
    Task<bool> TieneComprasRegistradasAsync(Guid bingoId);

    /// <summary>
    /// Persiste los cambios pendientes en el <c>DbContext</c> (spec FEAT-007, Block 2). Necesario
    /// porque <c>Bingo.Actualizar</c> muta la instancia ya trackeada devuelta por
    /// <see cref="ObtenerPorIdAsync"/> — no hay otro método de este puerto que persista esa
    /// mutación.
    /// </summary>
    Task GuardarCambiosAsync();
}
