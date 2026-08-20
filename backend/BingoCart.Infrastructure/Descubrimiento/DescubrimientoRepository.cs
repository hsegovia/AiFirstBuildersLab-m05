using BingoCart.Application.Descubrimiento;
using BingoCart.Application.Descubrimiento.Dtos;
using BingoCart.Domain.Bingos;
using BingoCart.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BingoCart.Infrastructure.Descubrimiento;

/// <summary>
/// Implementa <see cref="IDescubrimientoRepository"/> (Application, Block 1 del spec FEAT-008a)
/// contra <see cref="AppDbContext"/> — capa de infraestructura pura, sin decisiones de negocio (qué
/// es "activo" ya viene resuelto por el filtro SQL explícito; cuántos cartones pedir lo decide
/// Application en Block 2). Primer punto del proyecto que hace selección aleatoria a nivel de base
/// de datos: <c>ObtenerAleatoriosGlobalAsync</c>/<c>ObtenerAleatoriosDeBingoAsync</c> usan
/// <c>FromSqlInterpolated</c> con <c>ORDER BY NEWID()</c> (SQL Server) — nunca <c>FromSqlRaw</c>
/// con concatenación de string, ni <c>.OrderBy(_ => Guid.NewGuid())</c> (no traducible a SQL por EF
/// Core) (decisión de PLAN, ver spec). <c>ObtenerResumenBingosAsync</c> usa LINQ normal con un JOIN
/// a <c>Users</c>, mismo patrón que <c>DirectorioRepository</c> (FEAT-005).
/// </summary>
public sealed class DescubrimientoRepository : IDescubrimientoRepository
{
    private readonly AppDbContext _context;

    public DescubrimientoRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<Carton>> ObtenerAleatoriosGlobalAsync(DateTime ahoraUtc, int cantidad)
    {
        return await _context.Cartones
            .FromSqlInterpolated($@"
                SELECT TOP ({cantidad}) c.*
                FROM Cartones c
                INNER JOIN Bingos b ON b.Id = c.BingoId
                WHERE b.FechaSorteoUtc > {ahoraUtc}
                ORDER BY NEWID()")
            .AsNoTracking()
            .ToListAsync();
    }

    public Task<bool> ExisteOrganizadorAsync(Guid organizadorId)
    {
        return _context.Users.AnyAsync(u => u.Id == organizadorId);
    }

    public Task<Guid?> ObtenerBingoActivoDeOrganizadorAsync(Guid organizadorId, DateTime ahoraUtc)
    {
        return _context.Bingos
            .Where(b => b.OrganizadorId == organizadorId && b.FechaSorteoUtc > ahoraUtc)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefaultAsync();
    }

    public async Task<IReadOnlyList<Carton>> ObtenerAleatoriosDeBingoAsync(Guid bingoId, int cantidad)
    {
        return await _context.Cartones
            .FromSqlInterpolated($@"
                SELECT TOP ({cantidad}) c.*
                FROM Cartones c
                WHERE c.BingoId = {bingoId}
                ORDER BY NEWID()")
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<IReadOnlyList<BingoResumen>> ObtenerResumenBingosAsync(IReadOnlyCollection<Guid> bingoIds)
    {
        return await _context.Bingos
            .Join(_context.Users, b => b.OrganizadorId, u => u.Id, (b, u) => new { b, u })
            .Where(x => bingoIds.Contains(x.b.Id))
            .Select(x => new BingoResumen(x.b.Id, x.u.NombreOrganizacion, x.b.NombreEvento, x.b.CostoPorCarton, x.b.FechaSorteoUtc))
            .ToListAsync();
    }
}
