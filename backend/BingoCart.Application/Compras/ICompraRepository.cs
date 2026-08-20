using BingoCart.Domain.Compras;

namespace BingoCart.Application.Compras;

/// <summary>
/// Puerto de persistencia de <see cref="Compra"/> (spec FEAT-009a, Block 1). Infraestructura pura —
/// no decide negocio (qué compras se generan por organizador lo decide Application, Block 2, vía
/// <c>GroupBy(OrganizadorId)</c>).
/// </summary>
public interface ICompraRepository
{
    /// <summary>
    /// Persiste <paramref name="compras"/> en una única transacción EF Core ("todo o nada" real): si
    /// alguna viola el índice <c>UNIQUE</c> de <c>CompraCartones.CartonId</c> (carrera perdida contra
    /// otra confirmación), NINGUNA de las compras del intento queda persistida. Deja que
    /// <see cref="Microsoft.EntityFrameworkCore.DbUpdateException"/> se propague sin capturar —
    /// Application (Block 2) la traduce a <c>ReservaCarritoInvalidaException</c>.
    /// </summary>
    Task CrearVariasAsync(IReadOnlyList<Compra> compras);
}
