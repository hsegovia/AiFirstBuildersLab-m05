using BingoCart.Application.Compradores;
using BingoCart.Domain.Compras;
using BingoCart.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BingoCart.Infrastructure.Compradores;

/// <summary>
/// Implementa <see cref="ICompradorCuentaRepository"/> (Application, spec FEAT-009d Block 5) contra
/// <see cref="AppDbContext"/> — capa de infraestructura pura, sin lógica de negocio: la duración de
/// la ventana de 60 minutos la decide Application (<c>CompradorService</c>) vía
/// <c>TimeProvider</c>, este repositorio solo filtra contra los límites recibidos (mismo criterio
/// de "no guardar reglas de negocio en la capa de datos" de AGENTS.md).
/// </summary>
public sealed class CompradorCuentaRepository : ICompradorCuentaRepository
{
    private readonly AppDbContext _context;

    public CompradorCuentaRepository(AppDbContext context)
    {
        _context = context;
    }

    // JOIN normal (LINQ, no SQL crudo) Compras -> CompraCartones -> Cartones -> Bingos, mismo
    // patrón que IBingoRepository/IDescubrimientoRepository. Excluye compras Cancelado (D-02) y
    // filtra el sorteo contra la ventana [ahoraUtc, hastaUtc] recibida (D-01: los sorteos ya
    // pasados no bloquean porque ahoraUtc es el límite inferior).
    public Task<bool> TieneSorteoInminenteAsync(Guid compradorId, DateTime ahoraUtc, DateTime hastaUtc)
    {
        return _context.Compras
            .Where(c => c.CompradorId == compradorId && c.Estado != EstadoCompra.Cancelado)
            .Join(_context.CompraCartones, c => c.Id, cc => cc.CompraId, (c, cc) => cc.CartonId)
            .Join(_context.Cartones, cartonId => cartonId, ct => ct.Id, (cartonId, ct) => ct.BingoId)
            .Join(_context.Bingos, bingoId => bingoId, b => b.Id, (bingoId, b) => b)
            .AnyAsync(b => b.FechaSorteoUtc >= ahoraUtc && b.FechaSorteoUtc <= hastaUtc);
    }
}
