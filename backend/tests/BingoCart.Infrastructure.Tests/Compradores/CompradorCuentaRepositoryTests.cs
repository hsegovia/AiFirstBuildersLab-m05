using System;
using System.Linq;
using System.Threading.Tasks;
using BingoCart.Application.Compradores;
using BingoCart.Domain.Bingos;
using BingoCart.Domain.Compras;
using BingoCart.Infrastructure.Compradores;
using BingoCart.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BingoCart.Infrastructure.Tests.Compradores;

/// <summary>
/// Tests de integración de <see cref="CompradorCuentaRepository"/> contra SQL Server real (spec
/// FEAT-009d, Block 5) — mismo patrón que <c>BingoRepositoryTests</c>/<c>CompraRepositoryTests</c>:
/// base propia y descartable (<c>BingoCartTests_CompradorCuentaRepository</c>), migrada al inicio y
/// eliminada en <see cref="DisposeAsync"/> (Rule #0 de testing.instructions.md).
/// </summary>
public sealed class CompradorCuentaRepositoryTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Server=localhost,14330;Database=BingoCartTests_CompradorCuentaRepository;User Id=sa;" +
        "Password=BingoCart_Dev2026!;TrustServerCertificate=True;Encrypt=True;";

    private AppDbContext _context = null!;
    private ICompradorCuentaRepository _repository = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        _context = new AppDbContext(options);
        await _context.Database.MigrateAsync();

        _repository = new CompradorCuentaRepository(_context);
    }

    public async Task DisposeAsync()
    {
        await _context.Database.EnsureDeletedAsync();
        await _context.DisposeAsync();
    }

    private static Bingo NuevoBingo(DateTime fechaSorteoUtc, DateTime ahoraUtc) =>
        Bingo.Crear(
            nombreEvento: "Bingo de prueba",
            fechaSorteoUtc: fechaSorteoUtc,
            cantidadCartones: 10,
            costoPorCarton: 100m,
            organizadorId: Guid.NewGuid(),
            ahoraUtc: ahoraUtc);

    private static Carton NuevoCarton(Guid bingoId, int numeroCorrelativo, params int[] numeros) =>
        Carton.Crear(bingoId, numeros, numeroCorrelativo);

    private static Compra NuevaCompra(Guid compradorId, EstadoCompra estado, params Guid[] cartonIds)
    {
        var compra = Compra.Crear(
            Guid.NewGuid(),
            compradorId,
            Guid.NewGuid(),
            cartonIds.Select(id => new ItemCompra(id, 100m)).ToList(),
            MedioPago.Efectivo,
            DateTime.UtcNow);

        if (estado == EstadoCompra.Confirmado)
        {
            compra.ConfirmarPago();
        }
        else if (estado == EstadoCompra.Cancelado)
        {
            compra.Cancelar();
        }

        return compra;
    }

    private async Task SembrarAsync(Compra compra, Bingo bingo, Carton carton)
    {
        _context.Bingos.Add(bingo);
        _context.Cartones.Add(carton);
        _context.Compras.Add(compra);
        _context.CompraCartones.Add(new CompraCarton
        {
            CompraId = compra.Id,
            CartonId = carton.Id,
            PrecioUnitario = 100m,
        });

        await _context.SaveChangesAsync();
    }

    [Fact]
    public async Task TieneSorteoInminenteAsync_IgnoraComprasCanceladasYSorteosPasados()
    {
        var ahoraUtc = DateTime.UtcNow;
        var hastaUtc = ahoraUtc.AddMinutes(60);
        var compradorId = Guid.NewGuid();

        // (1) Sorteo dentro de la ventana, pero de una compra CANCELADA: no debe contar (D-02).
        var bingoCancelado = NuevoBingo(ahoraUtc.AddMinutes(30), ahoraUtc.AddDays(-5));
        var cartonCancelado = NuevoCarton(bingoCancelado.Id, 1, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10);
        var compraCancelada = NuevaCompra(compradorId, EstadoCompra.Cancelado, cartonCancelado.Id);
        await SembrarAsync(compraCancelada, bingoCancelado, cartonCancelado);

        // (2) Sorteo YA PASADO, de una compra vigente: no debe contar (D-01).
        var bingoPasado = NuevoBingo(ahoraUtc.AddDays(-1), ahoraUtc.AddDays(-10));
        var cartonPasado = NuevoCarton(bingoPasado.Id, 1, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20);
        var compraPasada = NuevaCompra(compradorId, EstadoCompra.Confirmado, cartonPasado.Id);
        await SembrarAsync(compraPasada, bingoPasado, cartonPasado);

        var tieneSorteoInminente = await _repository.TieneSorteoInminenteAsync(compradorId, ahoraUtc, hastaUtc);

        Assert.False(tieneSorteoInminente);

        // (3) Ahora se agrega un sorteo dentro de la ventana Y de una compra vigente: SÍ debe contar.
        var bingoInminente = NuevoBingo(ahoraUtc.AddMinutes(30), ahoraUtc.AddDays(-2));
        var cartonInminente = NuevoCarton(bingoInminente.Id, 1, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30);
        var compraInminente = NuevaCompra(compradorId, EstadoCompra.Confirmado, cartonInminente.Id);
        await SembrarAsync(compraInminente, bingoInminente, cartonInminente);

        var tieneSorteoInminenteAhora = await _repository.TieneSorteoInminenteAsync(compradorId, ahoraUtc, hastaUtc);

        Assert.True(tieneSorteoInminenteAhora);
    }
}
