using BingoCart.Application.Bingos;
using BingoCart.Domain.Bingos;
using BingoCart.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BingoCart.Infrastructure.Bingos;

/// <summary>
/// Implementa <see cref="IBingoRepository"/> (Application, Block 3 del spec FEAT-003) contra
/// <see cref="AppDbContext"/> — capa de infraestructura pura, sin lógica de negocio: delega toda
/// decisión (si un bingo está "activo", qué cartones generar) a quien la invoque (Block 4).
/// </summary>
public sealed class BingoRepository : IBingoRepository
{
    private readonly AppDbContext _context;

    public BingoRepository(AppDbContext context)
    {
        _context = context;
    }

    public Task<bool> TieneBingoActivoAsync(Guid organizadorId, DateTime ahoraUtc) =>
        _context.Bingos.AnyAsync(b => b.OrganizadorId == organizadorId && b.FechaSorteoUtc > ahoraUtc);

    public async Task CrearAsync(Bingo bingo, IReadOnlyList<Carton> cartones)
    {
        _context.Bingos.Add(bingo);
        _context.Cartones.AddRange(cartones);

        await _context.SaveChangesAsync();
    }

    public async Task<BingosPaginados> ListarPorOrganizadorAsync(Guid organizadorId, int page, int pageSize)
    {
        var query = _context.Bingos.Where(b => b.OrganizadorId == organizadorId);

        var total = await query.CountAsync();
        var bingos = await query
            .OrderByDescending(b => b.FechaCreacionUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new BingosPaginados(bingos, total);
    }
}
