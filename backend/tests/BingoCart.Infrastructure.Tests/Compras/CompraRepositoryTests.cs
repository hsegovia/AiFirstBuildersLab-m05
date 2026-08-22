using System;
using System.Linq;
using System.Threading.Tasks;
using BingoCart.Application.Compras;
using BingoCart.Domain.Compras;
using BingoCart.Domain.Compras.Exceptions;
using BingoCart.Infrastructure.Compras;
using BingoCart.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BingoCart.Infrastructure.Tests.Compras;

/// <summary>
/// Tests de integración de <see cref="CompraRepository"/> contra SQL Server real (spec FEAT-009a,
/// Block 1) — mismo patrón que <c>BingoRepositoryTests</c>: base propia y descartable
/// (<c>BingoCartTests_CompraRepository</c>), migrada al inicio y eliminada en
/// <see cref="DisposeAsync"/> (Rule #0 de testing.instructions.md).
/// </summary>
public sealed class CompraRepositoryTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Server=localhost,14330;Database=BingoCartTests_CompraRepository;User Id=sa;" +
        "Password=BingoCart_Dev2026!;TrustServerCertificate=True;Encrypt=True;";

    private AppDbContext _context = null!;
    private ICompraRepository _repository = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        _context = new AppDbContext(options);
        await _context.Database.MigrateAsync();

        _repository = new CompraRepository(_context);
    }

    public async Task DisposeAsync()
    {
        await _context.Database.EnsureDeletedAsync();
        await _context.DisposeAsync();
    }

    private static Compra NuevaCompra(Guid organizadorId, Guid compradorId, MedioPago medioPago, params Guid[] cartonIds) =>
        Compra.Crear(
            organizadorId,
            compradorId,
            Guid.NewGuid(),
            cartonIds.Select(id => new ItemCompra(id, 100m)).ToList(),
            medioPago,
            DateTime.UtcNow);

    [Fact]
    public async Task CrearVariasAsync_ConDosComprasDeOrganizadoresDistintosYMediosDePagoDistintos_AmbasQuedanPersistidasCorrectamente()
    {
        var compradorId = Guid.NewGuid();
        var organizadorUno = Guid.NewGuid();
        var organizadorDos = Guid.NewGuid();
        var cartonUno = Guid.NewGuid();
        var cartonDos = Guid.NewGuid();

        var compraTransferencia = NuevaCompra(organizadorUno, compradorId, MedioPago.Transferencia, cartonUno);
        var compraEfectivo = NuevaCompra(organizadorDos, compradorId, MedioPago.Efectivo, cartonDos);

        await _repository.CrearVariasAsync(new[] { compraTransferencia, compraEfectivo });

        var comprasPersistidas = await _context.Compras.Where(c => c.CompradorId == compradorId).ToListAsync();
        var itemsPersistidos = await _context.CompraCartones
            .Where(cc => cc.CartonId == cartonUno || cc.CartonId == cartonDos)
            .ToListAsync();

        Assert.Equal(2, comprasPersistidas.Count);
        Assert.All(comprasPersistidas, c => Assert.Equal(EstadoCompra.PendienteConfirmacionPago, c.Estado));
        Assert.Contains(comprasPersistidas, c => c.OrganizadorId == organizadorUno && c.MedioPago == MedioPago.Transferencia);
        Assert.Contains(comprasPersistidas, c => c.OrganizadorId == organizadorDos && c.MedioPago == MedioPago.Efectivo);

        Assert.Equal(2, itemsPersistidos.Count);
        Assert.Contains(itemsPersistidos, i => i.CartonId == cartonUno && i.CompraId == compraTransferencia.Id);
        Assert.Contains(itemsPersistidos, i => i.CartonId == cartonDos && i.CompraId == compraEfectivo.Id);
    }

    [Fact]
    public async Task CrearVariasAsync_ConCartonIdQueYaExisteEnCompraCartones_LanzaReservaCarritoInvalidaExceptionYNoPersisteNadaDelIntentoActual()
    {
        var cartonYaVendido = Guid.NewGuid();
        var compraPrevia = NuevaCompra(Guid.NewGuid(), Guid.NewGuid(), MedioPago.Efectivo, cartonYaVendido);

        // Sembrada con un DbContext PROPIO y descartado (mismo criterio que un request HTTP previo
        // real, con su propio scope) — evita que el identity map del `_context` del test detecte un
        // conflicto de tracking client-side (InvalidOperationException) al intentar trackear un
        // SEGUNDO `CompraCarton` con el mismo `CartonId` en el MISMO DbContext; la violación real que
        // este test valida es la del índice `UNIQUE`/PK en SQL Server, no un conflicto de tracking.
        var optionsSiembra = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;
        await using (var contextoSiembra = new AppDbContext(optionsSiembra))
        {
            await new CompraRepository(contextoSiembra).CrearVariasAsync(new[] { compraPrevia });
        }

        var organizadorNuevoIntento = Guid.NewGuid();
        var compraDelIntentoActual = NuevaCompra(organizadorNuevoIntento, Guid.NewGuid(), MedioPago.Transferencia, cartonYaVendido);

        // Corrective round 2: la traducción DbUpdateException -> ReservaCarritoInvalidaException
        // ahora vive en Infrastructure (antes en Application, spec FEAT-009a Block 2) — Application
        // no puede depender de Microsoft.EntityFrameworkCore (AGENTS.md, "Layer separation").
        await Assert.ThrowsAsync<ReservaCarritoInvalidaException>(() =>
            _repository.CrearVariasAsync(new[] { compraDelIntentoActual }));

        var compraDelIntentoPersistida = await _context.Compras
            .AnyAsync(c => c.OrganizadorId == organizadorNuevoIntento);

        Assert.False(compraDelIntentoPersistida);
    }

    // Spec FEAT-009c, Block 3: ObtenerPorIdAsync/GuardarCambiosAsync/ListarPorOrganizadorAsync,
    // implementaciones reales contra SQL Server que reemplazan el CS0535 de partida de este bloque.

    [Fact]
    public async Task ObtenerPorIdAsync_ConIdExistente_DevuelveLaCompraTrackeada()
    {
        var compra = NuevaCompra(Guid.NewGuid(), Guid.NewGuid(), MedioPago.Efectivo, Guid.NewGuid());
        _context.Compras.Add(compra);
        await _context.SaveChangesAsync();

        var resultado = await _repository.ObtenerPorIdAsync(compra.Id);

        Assert.NotNull(resultado);
        Assert.Equal(compra.Id, resultado!.Id);
        // "Trackeada" (spec: mismo patrón que IBingoRepository.ObtenerPorIdAsync, sin AsNoTracking):
        // el ChangeTracker del mismo DbContext debe reportar la entidad, no un objeto suelto.
        Assert.Equal(EntityState.Unchanged, _context.Entry(resultado).State);
    }

    [Fact]
    public async Task ObtenerPorIdAsync_ConIdInexistente_DevuelveNull()
    {
        var resultado = await _repository.ObtenerPorIdAsync(Guid.NewGuid());

        Assert.Null(resultado);
    }

    [Fact]
    public async Task GuardarCambiosAsync_TrasMutarUnaEntidadObtenida_PersisteElCambio()
    {
        var compra = NuevaCompra(Guid.NewGuid(), Guid.NewGuid(), MedioPago.Efectivo, Guid.NewGuid());
        _context.Compras.Add(compra);
        await _context.SaveChangesAsync();

        var compraTrackeada = await _repository.ObtenerPorIdAsync(compra.Id);
        compraTrackeada!.ConfirmarPago();
        await _repository.GuardarCambiosAsync();

        // Contra SQL Server real, con un DbContext NUEVO (no el mismo _context, para no leer del
        // identity map en memoria — la aserción real es que la fila en la base cambió).
        var optionsNuevo = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;
        await using var contextoNuevo = new AppDbContext(optionsNuevo);
        var persistida = await contextoNuevo.Compras.SingleAsync(c => c.Id == compra.Id);

        Assert.Equal(EstadoCompra.Confirmado, persistida.Estado);
    }

    [Fact]
    public async Task ListarPorOrganizadorAsync_ConTresComprasYPageSizeDos_DevuelveDosYTotalTres()
    {
        var organizadorId = Guid.NewGuid();
        var compradorId = Guid.NewGuid();
        var compras = new[]
        {
            NuevaCompra(organizadorId, compradorId, MedioPago.Efectivo, Guid.NewGuid()),
            NuevaCompra(organizadorId, compradorId, MedioPago.Efectivo, Guid.NewGuid()),
            NuevaCompra(organizadorId, compradorId, MedioPago.Transferencia, Guid.NewGuid()),
        };
        await _repository.CrearVariasAsync(compras);

        var resultado = await _repository.ListarPorOrganizadorAsync(organizadorId, page: 1, pageSize: 2);

        Assert.Equal(2, resultado.Items.Count);
        Assert.Equal(3, resultado.Total);
    }

    // Corrección post-implementación de Block 3: ConfirmarPagoAsync/CancelarAsync
    // (CompraOrganizadorService, Application) necesitan devolver un CompraResumenResponse completo
    // sin depender de que la compra recién mutada caiga en la primera página de
    // ListarPorOrganizadorAsync.

    [Fact]
    public async Task ObtenerMontoTotalAsync_ConDosCartones_SumaAmbosPrecios()
    {
        var compra = NuevaCompra(Guid.NewGuid(), Guid.NewGuid(), MedioPago.Efectivo, Guid.NewGuid(), Guid.NewGuid());
        await _repository.CrearVariasAsync(new[] { compra });

        var montoTotal = await _repository.ObtenerMontoTotalAsync(compra.Id);

        Assert.Equal(200m, montoTotal);
    }

    [Fact]
    public async Task ObtenerMontoTotalAsync_ConCompraSinCartones_DevuelveCero()
    {
        var montoTotal = await _repository.ObtenerMontoTotalAsync(Guid.NewGuid());

        Assert.Equal(0m, montoTotal);
    }

    // Corrective loop de VERIFY (F-VER-03): ListarPorOrganizadorAsync tenía branch coverage <80% —
    // faltaba ejercitar la rama ": 0m" del TryGetValue sobre montosPorCompra cuando una compra de la
    // página no tiene ninguna fila en CompraCartones.

    [Fact]
    public async Task ListarPorOrganizadorAsync_ConUnaCompraSinCartones_DevuelveMontoCero()
    {
        // `Compra.Items` está ignorado por AppDbContext (el monto vive en CompraCartones, no en la
        // entidad) — agregar la Compra directamente (sin pasar por CrearVariasAsync, que sí escribe
        // CompraCartones) deja la compra persistida sin ninguna fila asociada, igual que el caso real
        // "página con una compra ya cancelada/legacy sin ítems".
        var organizadorId = Guid.NewGuid();
        var compraSinCartones = NuevaCompra(organizadorId, Guid.NewGuid(), MedioPago.Efectivo, Guid.NewGuid());
        _context.Compras.Add(compraSinCartones);
        await _context.SaveChangesAsync();

        var resultado = await _repository.ListarPorOrganizadorAsync(organizadorId, page: 1, pageSize: 10);

        var item = Assert.Single(resultado.Items);
        Assert.Equal(compraSinCartones.Id, item.Compra.Id);
        Assert.Equal(0m, item.MontoTotal);
    }
}
