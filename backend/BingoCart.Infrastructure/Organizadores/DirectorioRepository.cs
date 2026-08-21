using BingoCart.Application.Organizadores;
using BingoCart.Application.Organizadores.Dtos;
using BingoCart.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BingoCart.Infrastructure.Organizadores;

/// <summary>
/// Implementa <see cref="IDirectorioRepository"/> (Application, Block 1 del spec FEAT-005) contra
/// <see cref="AppDbContext"/> — capa de infraestructura pura, sin decisiones de negocio (qué es
/// "activo", el máximo de <c>pageSize</c>): eso lo decide Application (Block 2). Único punto del
/// proyecto que cruza <c>Bingos</c> con la tabla de Identity (<c>AppDbContext.Users</c>), vía
/// <c>OrganizadorId == Id</c> (FK lógica, sin <c>HasForeignKey</c> de EF Core — mismo criterio ya
/// usado para <c>Bingo.OrganizadorId</c>, confirmado en FEAT-003). La proyección con
/// <c>Select()</c> es LINQ estrictamente tipada a <see cref="DirectorioOrganizadorItem"/> (4
/// campos, incluido <c>Id</c> desde spec FEAT-008a) — nunca materializa <c>ApplicationUser</c>
/// completo (mitigación R-01 del threat model).
/// </summary>
public sealed class DirectorioRepository : IDirectorioRepository
{
    private readonly AppDbContext _context;

    public DirectorioRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<DirectorioPaginado> ListarActivosAsync(DateTime ahoraUtc, int page, int pageSize)
    {
        var query = _context.Bingos
            .Join(_context.Users, b => b.OrganizadorId, u => u.Id, (b, u) => new { b, u })
            .Where(x => x.b.FechaSorteoUtc > ahoraUtc);

        var total = await query.CountAsync();

        var items = await query
            .OrderBy(x => x.b.FechaSorteoUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            // `NombreOrganizacion` es nullable en el esquema desde FEAT-009a (el comprador no lo
            // completa), pero acá `u` siempre proviene de `Bingo.OrganizadorId` — un organizador,
            // que sí lo completa siempre — el `!` es seguro, no oculta un caso real de nulidad.
            .Select(x => new DirectorioOrganizadorItem(x.u.Id, x.u.NombreOrganizacion!, x.b.NombreEvento, x.b.FechaSorteoUtc))
            .ToListAsync();

        return new DirectorioPaginado(items, total);
    }
}
