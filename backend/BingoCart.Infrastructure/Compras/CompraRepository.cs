using BingoCart.Application.Compras;
using BingoCart.Domain.Compras;
using BingoCart.Domain.Compras.Exceptions;
using BingoCart.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BingoCart.Infrastructure.Compras;

/// <summary>
/// Implementa <see cref="ICompraRepository"/> (Application, Block 1 del spec FEAT-009a) contra
/// <see cref="AppDbContext"/> — capa de infraestructura pura, sin lógica de negocio: qué compras se
/// generan por organizador ya viene decidido por quien la invoque (Application, Block 2).
/// </summary>
public sealed class CompraRepository : ICompraRepository
{
    private readonly AppDbContext _context;

    public CompraRepository(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Persiste <paramref name="compras"/> y sus <c>ItemCompra</c> (como filas de
    /// <c>CompraCartones</c>) en una única transacción EF Core — "todo o nada" real (NFR-01): si
    /// algún <c>CartonId</c> ya existe en <c>CompraCartones</c> (violación del índice/PK
    /// <c>UNIQUE</c>), se traduce acá mismo a <see cref="ReservaCarritoInvalidaException"/> (corrective
    /// round 2 de FEAT-009a Block 2: Application no puede depender de EF Core — AGENTS.md, "Layer
    /// separation" — así que la traducción se mueve a Infrastructure, que ya conoce tanto EF Core como
    /// Domain) y NINGUNA de las compras del intento queda persistida.
    /// </summary>
    public async Task CrearVariasAsync(IReadOnlyList<Compra> compras)
    {
        await using var transaccion = await _context.Database.BeginTransactionAsync();

        _context.Compras.AddRange(compras);

        var itemsCompra = compras.SelectMany(compra => compra.Items.Select(item => new CompraCarton
        {
            CompraId = compra.Id,
            CartonId = item.CartonId,
            PrecioUnitario = item.PrecioUnitario,
        }));
        _context.CompraCartones.AddRange(itemsCompra);

        try
        {
            await _context.SaveChangesAsync();
            await transaccion.CommitAsync();
        }
        catch (DbUpdateException)
        {
            // Decisión de PLAN (spec FEAT-009a, Block 2): no hay forma barata de saber cuál
            // CartonId violó el UNIQUE sin una consulta extra — se documenta como limitación
            // aceptada y se reporta la lista vacía.
            throw new ReservaCarritoInvalidaException(
                Array.Empty<Guid>(),
                "La confirmación perdió la carrera contra otra compra concurrente.");
        }
    }
}
