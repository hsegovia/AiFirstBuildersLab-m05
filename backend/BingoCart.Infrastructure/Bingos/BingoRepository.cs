using BingoCart.Application.Bingos;
using BingoCart.Application.Carritos.Dtos;
using BingoCart.Application.Compras.Dtos;
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

    public Task<Bingo?> ObtenerPorIdAsync(Guid id) =>
        _context.Bingos.FirstOrDefaultAsync(b => b.Id == id);

    public async Task EliminarAsync(Bingo bingo)
    {
        _context.Bingos.Remove(bingo);
        await _context.SaveChangesAsync();
    }

    // Implementación real (spec FEAT-009a, Block 1) — reemplaza el `false` hardcodeado desde
    // FEAT-007. EXISTS contra CompraCartones join Cartones por BingoId: un bingo "tiene compras" si
    // al menos uno de sus cartones aparece en CompraCartones.
    public Task<bool> TieneComprasRegistradasAsync(Guid bingoId) =>
        _context.CompraCartones.AnyAsync(cc =>
            _context.Cartones.Any(c => c.Id == cc.CartonId && c.BingoId == bingoId));

    public Task GuardarCambiosAsync() => _context.SaveChangesAsync();

    // Spec FEAT-008b, Block 2: JOIN normal (LINQ, no SQL crudo) contra Cartones+Bingos+Users, mismo
    // patrón que DescubrimientoRepository.ObtenerResumenBingosAsync. `null` sin distinguir "cartón
    // inexistente" de "bingo vencido" — mismo criterio ya usado en BingoNoEncontradoException.
    public Task<CartonParaCarrito?> ObtenerParaCarritoAsync(Guid cartonId, DateTime ahoraUtc)
    {
        return _context.Cartones
            .Join(_context.Bingos, c => c.BingoId, b => b.Id, (c, b) => new { c, b })
            .Join(_context.Users, cb => cb.b.OrganizadorId, u => u.Id, (cb, u) => new { cb.c, cb.b, u })
            .Where(x => x.c.Id == cartonId
                && x.b.FechaSorteoUtc > ahoraUtc
                // FR-07 (spec FEAT-009a, Block 1): un cartón ya vendido nunca puede volver a
                // agregarse a un carrito, aunque su bingo siga activo.
                && !_context.CompraCartones.Any(cc => cc.CartonId == x.c.Id))
            // `!` seguro: `u` siempre proviene de `Bingo.OrganizadorId`, un organizador, que
            // siempre completa `NombreOrganizacion` (mismo criterio que ObtenerParaConfirmarCompraAsync).
            .Select(x => new CartonParaCarrito(x.c.Id, x.b.Id, x.b.CostoPorCarton, x.u.NombreOrganizacion!, x.b.NombreEvento))
            .FirstOrDefaultAsync();
    }

    // Spec FEAT-009a, Block 1: JOIN normal (LINQ) contra Cartones+Bingos+Users, sin filtro de
    // "bingo activo" — a diferencia de ObtenerParaCarritoAsync, acá el cartón ya pasó la
    // revalidación de reserva (RevalidarReservasAsync); si su bingo venció en el ínterin es un caso
    // de borde aceptado, fuera de alcance de este ticket.
    public async Task<IReadOnlyList<CartonParaConfirmarCompra>> ObtenerParaConfirmarCompraAsync(
        IReadOnlyCollection<Guid> cartonIds)
    {
        return await _context.Cartones
            .Where(c => cartonIds.Contains(c.Id))
            .Join(_context.Bingos, c => c.BingoId, b => b.Id, (c, b) => new { c, b })
            .Join(_context.Users, cb => cb.b.OrganizadorId, u => u.Id, (cb, u) => new { cb.c, cb.b, u })
            // `NombreOrganizacion` es nullable en el esquema (comprador no lo completa), pero acá
            // `u` siempre proviene de `Bingo.OrganizadorId` — un organizador, que sí lo completa
            // siempre (`OrganizadorService.RegistrarAsync`, invariante nunca relajada por este
            // ticket) — el `!` es seguro, no oculta un caso real de nulidad.
            .Select(x => new CartonParaConfirmarCompra(x.c.Id, x.b.Id, x.b.OrganizadorId, x.u.NombreOrganizacion!, x.b.NombreEvento))
            .ToListAsync();
    }
}
