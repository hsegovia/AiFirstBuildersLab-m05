using BingoCart.Application.Compras;
using BingoCart.Application.Compras.Dtos;
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

    // Trackeada (sin AsNoTracking, spec FEAT-009c Block 3): mismo patrón que
    // BingoRepository.ObtenerPorIdAsync — la mutación posterior vía Compra.ConfirmarPago/Cancelar
    // (Domain) queda detectada por EF Core sin un Update() explícito, y GuardarCambiosAsync (abajo)
    // la persiste.
    public Task<Compra?> ObtenerPorIdAsync(Guid id) =>
        _context.Compras.FirstOrDefaultAsync(c => c.Id == id);

    public Task GuardarCambiosAsync() => _context.SaveChangesAsync();

    /// <summary>
    /// Mirror de la paginación de <c>BingoRepository.ListarPorOrganizadorAsync</c> (mismo orden
    /// descendente por <c>FechaCreacionUtc</c>, <c>Skip</c>/<c>Take</c>, <c>CountAsync</c> para el
    /// total sin paginar). El monto de cada compra NO vive en <see cref="Compra"/>
    /// (<c>AppDbContext</c> mapea <c>entity.Ignore(c =&gt; c.Items)</c>) — se calcula acá con un
    /// <c>GroupBy(CompraId)</c> + <c>Sum(PrecioUnitario)</c> sobre <c>CompraCartones</c>, restringido
    /// a las compras de la página actual (nunca sobre toda la tabla), y se compone en memoria con las
    /// <see cref="Compra"/> ya paginadas — mismo estilo "múltiples queries compuestas en
    /// Infrastructure" ya usado en <c>EnvioMailRepository.ObtenerDatosParaEnviarAsync</c>.
    /// </summary>
    public async Task<ComprasPaginadas> ListarPorOrganizadorAsync(Guid organizadorId, int page, int pageSize)
    {
        var query = _context.Compras.Where(c => c.OrganizadorId == organizadorId);

        var total = await query.CountAsync();
        var compras = await query
            .OrderByDescending(c => c.FechaCreacionUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var compraIds = compras.Select(c => c.Id).ToList();
        var montosPorCompra = await _context.CompraCartones
            .Where(cc => compraIds.Contains(cc.CompraId))
            .GroupBy(cc => cc.CompraId)
            .Select(g => new { CompraId = g.Key, MontoTotal = g.Sum(cc => cc.PrecioUnitario) })
            .ToDictionaryAsync(x => x.CompraId, x => x.MontoTotal);

        var items = compras
            .Select(compra => new CompraConMonto(
                compra,
                montosPorCompra.TryGetValue(compra.Id, out var monto) ? monto : 0m))
            .ToList();

        return new ComprasPaginadas(items, total);
    }

    public async Task<decimal> ObtenerMontoTotalAsync(Guid compraId) =>
        await _context.CompraCartones
            .Where(cc => cc.CompraId == compraId)
            .SumAsync(cc => (decimal?)cc.PrecioUnitario) ?? 0m;

    /// <summary>
    /// Join de 4 tablas (spec FEAT-009d, Block 3): <c>CompraCartones</c> → <c>Compras</c> (por
    /// <c>CompraId</c>) → <c>Cartones</c> (por <c>CartonId</c>) → <c>Bingos</c> → <c>AspNetUsers</c>,
    /// filtrado por <c>Compras.CompradorId</c> — mismo precedente de join LINQ tipado que
    /// <c>BingoRepository.ObtenerParaConfirmarCompraAsync</c>, combinado con la paginación
    /// (<c>CountAsync</c> + <c>Skip</c>/<c>Take</c> + <c>Select</c> proyectado) de
    /// <c>DirectorioRepository.ListarActivosAsync</c>. Orden: <c>Compra.FechaCreacionUtc</c>
    /// descendente, desempatado por <c>Carton.NumeroCorrelativo</c> ascendente (esa columna no es
    /// única — dos cartones de la misma compra comparten fecha — así que sin desempate la
    /// paginación no sería estable entre páginas).
    /// </summary>
    public async Task<CartonesAdquiridosPaginados> ListarCartonesDelCompradorAsync(Guid compradorId, int page, int pageSize)
    {
        var query = _context.CompraCartones
            .Join(_context.Compras, cc => cc.CompraId, c => c.Id, (cc, c) => new { cc, c })
            .Where(x => x.c.CompradorId == compradorId)
            .Join(_context.Cartones, x => x.cc.CartonId, carton => carton.Id, (x, carton) => new { x.c, carton })
            .Join(_context.Bingos, x => x.carton.BingoId, b => b.Id, (x, b) => new { x.c, x.carton, b })
            .Join(_context.Users, x => x.b.OrganizadorId, u => u.Id, (x, u) => new { x.c, x.carton, x.b, u });

        var total = await query.CountAsync();

        // Proyección parcial: nunca materializa Carton/Bingo/ApplicationUser completos, mismo
        // criterio que DirectorioRepository.ListarActivosAsync. `Estado` se lleva como el enum
        // (mapeado por convención de EF Core) y se traduce a `string` DESPUÉS, en memoria, sobre la
        // página ya acotada (máximo 50 filas) — mismo criterio que
        // CompraOrganizadorService.ListarPropiasAsync: Enum.ToString() no traduce a SQL de forma
        // portable, así que la conversión ocurre fuera de la query.
        var pagina = await query
            .OrderByDescending(x => x.c.FechaCreacionUtc)
            .ThenBy(x => x.carton.NumeroCorrelativo)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                CartonId = x.carton.Id,
                x.carton.NumeroCorrelativo,
                x.carton.Numeros,
                x.b.NombreEvento,
                // `NombreOrganizacion` es nullable en el esquema (el comprador no lo completa), pero
                // acá `u` siempre proviene de `Bingo.OrganizadorId` — un organizador, que sí lo
                // completa siempre — el `!` es seguro, mismo criterio que
                // ObtenerParaConfirmarCompraAsync.
                NombreOrganizacion = x.u.NombreOrganizacion!,
                x.c.Estado,
                CompraId = x.c.Id,
            })
            .ToListAsync();

        var items = pagina
            .Select(x => new CartonAdquiridoResponse(
                x.CartonId,
                x.NumeroCorrelativo,
                x.Numeros,
                x.NombreEvento,
                x.NombreOrganizacion,
                x.Estado.ToString(),
                x.CompraId))
            .ToList();

        return new CartonesAdquiridosPaginados(items, total);
    }
}
