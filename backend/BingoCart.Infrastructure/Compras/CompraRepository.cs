using BingoCart.Application.Compras;
using BingoCart.Domain.Compras;
using BingoCart.Infrastructure.Data;

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
    /// <c>UNIQUE</c>), <see cref="Microsoft.EntityFrameworkCore.DbUpdateException"/> se propaga sin
    /// capturar y NINGUNA de las compras del intento queda persistida.
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

        await _context.SaveChangesAsync();
        await transaccion.CommitAsync();
    }
}
