using System;
using System.Linq;
using System.Threading.Tasks;
using BingoCart.Application.Compras;
using BingoCart.Domain.Compras;
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
    public async Task CrearVariasAsync_ConCartonIdQueYaExisteEnCompraCartones_LanzaDbUpdateExceptionYNoPersisteNadaDelIntentoActual()
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

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            _repository.CrearVariasAsync(new[] { compraDelIntentoActual }));

        var compraDelIntentoPersistida = await _context.Compras
            .AnyAsync(c => c.OrganizadorId == organizadorNuevoIntento);

        Assert.False(compraDelIntentoPersistida);
    }
}
