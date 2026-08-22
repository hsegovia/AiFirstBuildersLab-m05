using BingoCart.Application.Compras;
using BingoCart.Application.Compras.Dtos;
using BingoCart.Domain.Compras;
using BingoCart.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BingoCart.Infrastructure.Compras;

/// <summary>
/// Implementa <see cref="IEnvioMailRepository"/> (Application, Block 2 del spec FEAT-009b) contra
/// <see cref="AppDbContext"/> — capa de infraestructura pura, sin lógica de negocio: la máquina de
/// estados de reintentos vive en <see cref="EnvioMail"/> (Domain, Block 1), y qué envíos están
/// "listos" ya viene resuelto por el filtro SQL explícito de <see cref="ObtenerPendientesAsync"/>.
/// </summary>
public sealed class EnvioMailRepository : IEnvioMailRepository
{
    private readonly AppDbContext _context;

    public EnvioMailRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task EncolarAsync(EnvioMail envio)
    {
        _context.EnviosMail.Add(envio);
        await _context.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<EnvioMail>> ObtenerPendientesAsync(DateTime ahoraUtc)
    {
        return await _context.EnviosMail
            .Where(e => e.Estado == EstadoEnvioMail.Pendiente
                && (e.ProximoIntentoUtc == null || e.ProximoIntentoUtc <= ahoraUtc))
            .ToListAsync();
    }

    /// <summary>
    /// Resuelve, en 3 consultas separadas (no un único LINQ traducido a un solo JOIN de SQL): las
    /// <c>Compra</c> con <paramref name="confirmacionId"/>, el comprador (vía <c>AppDbContext.Users</c>,
    /// mismo cruce con Identity ya usado en <c>DirectorioRepository</c>/<c>DescubrimientoRepository</c>)
    /// y los ítems de cada compra (join a <c>CompraCartones</c>/<c>Cartones</c> para los números).
    /// Composición en memoria después de materializar — simple de leer, y el volumen por
    /// confirmación es acotado (las compras de un único checkout, nunca miles de filas).
    /// </summary>
    public async Task<DatosParaMailConfirmacion?> ObtenerDatosParaEnviarAsync(Guid confirmacionId)
    {
        var compras = await _context.Compras
            .Where(c => c.ConfirmacionId == confirmacionId)
            .AsNoTracking()
            .ToListAsync();

        if (compras.Count == 0)
        {
            // Caso defensivo esperado (ver IEnvioMailRepository): ninguna Compra con ese
            // ConfirmacionId — null, no una excepción.
            return null;
        }

        var compradorId = compras[0].CompradorId;
        var comprador = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == compradorId);
        if (comprador is null)
        {
            return null;
        }

        var compraIds = compras.Select(c => c.Id).ToList();
        var itemsPorCompra = await _context.CompraCartones
            .Where(cc => compraIds.Contains(cc.CompraId))
            .Join(_context.Cartones, cc => cc.CartonId, carton => carton.Id,
                (cc, carton) => new { cc.CompraId, cc.PrecioUnitario, CartonId = carton.Id, carton.Numeros })
            .AsNoTracking()
            .ToListAsync();

        var organizadorIds = compras.Select(c => c.OrganizadorId).Distinct().ToList();
        var nombresOrganizadores = await _context.Users
            .Where(u => organizadorIds.Contains(u.Id))
            .AsNoTracking()
            .ToDictionaryAsync(u => u.Id, u => u.NombreOrganizacion ?? string.Empty);

        var comprasParaMail = compras
            .Select(compra =>
            {
                var items = itemsPorCompra.Where(i => i.CompraId == compra.Id).ToList();
                var cartones = items
                    .Select(i => new CartonParaMail(i.CartonId, i.Numeros))
                    .ToList();
                var montoTotal = items.Sum(i => i.PrecioUnitario);
                var nombreOrganizacion = nombresOrganizadores.TryGetValue(compra.OrganizadorId, out var nombre)
                    ? nombre
                    : string.Empty;

                return new CompraParaMail(compra.Id, nombreOrganizacion, montoTotal, cartones);
            })
            .ToList();

        return new DatosParaMailConfirmacion(
            comprador.Email ?? string.Empty,
            comprador.Nombre ?? string.Empty,
            comprador.Apellido ?? string.Empty,
            comprasParaMail);
    }

    public async Task ActualizarAsync(EnvioMail envio)
    {
        _context.EnviosMail.Update(envio);
        await _context.SaveChangesAsync();
    }
}
