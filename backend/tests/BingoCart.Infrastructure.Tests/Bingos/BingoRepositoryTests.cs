using System;
using System.Linq;
using System.Threading.Tasks;
using BingoCart.Application.Bingos;
using BingoCart.Domain.Bingos;
using BingoCart.Infrastructure.Bingos;
using BingoCart.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BingoCart.Infrastructure.Tests.Bingos;

/// <summary>
/// Tests de integración de <see cref="BingoRepository"/> contra SQL Server real (spec FEAT-003,
/// Block 3) — mismo patrón que <c>AppDbContextTests</c>: base propia y descartable
/// (<c>BingoCartTests_BingoRepository</c>), migrada al inicio y eliminada en <see cref="DisposeAsync"/>
/// (Rule #0 de testing.instructions.md). <c>OrganizadorId</c> es una FK LÓGICA (ver Data model del
/// spec: "FK lógica a AspNetUsers.Id, indexada, no única" — no hay `HasForeignKey` hacia
/// `AspNetUsers` en el Fluent API de `Bingo`), por lo que estos tests usan `Guid.NewGuid()` sueltos
/// sin necesitar crear un `ApplicationUser` real para cada organizador de prueba.
/// </summary>
public sealed class BingoRepositoryTests : IAsyncLifetime
{
    // Mismo password de desarrollo local declarado en docker-compose.yml — nunca es un secreto
    // real, solo la credencial del contenedor SQL Server efímero de desarrollo.
    private const string ConnectionString =
        "Server=localhost,14330;Database=BingoCartTests_BingoRepository;User Id=sa;" +
        "Password=BingoCart_Dev2026!;TrustServerCertificate=True;Encrypt=True;";

    private AppDbContext _context = null!;
    private IBingoRepository _repository = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        _context = new AppDbContext(options);
        await _context.Database.MigrateAsync();

        _repository = new BingoRepository(_context);
    }

    public async Task DisposeAsync()
    {
        await _context.Database.EnsureDeletedAsync();
        await _context.DisposeAsync();
    }

    private static Bingo NuevoBingo(Guid organizadorId, DateTime fechaSorteoUtc, DateTime ahoraUtc) =>
        Bingo.Crear(
            nombreEvento: "Bingo de prueba",
            fechaSorteoUtc: fechaSorteoUtc,
            cantidadCartones: 10,
            costoPorCarton: 100m,
            organizadorId: organizadorId,
            ahoraUtc: ahoraUtc);

    private static Carton NuevoCarton(Guid bingoId, params int[] numeros) =>
        Carton.Crear(bingoId, numeros);

    [Fact]
    public async Task TieneBingoActivoAsync_ConBingoDeFechaSorteoFutura_DevuelveTrue()
    {
        var organizadorId = Guid.NewGuid();
        var ahoraUtc = DateTime.UtcNow;
        var bingo = NuevoBingo(organizadorId, ahoraUtc.AddDays(5), ahoraUtc);

        _context.Bingos.Add(bingo);
        await _context.SaveChangesAsync();

        var resultado = await _repository.TieneBingoActivoAsync(organizadorId, ahoraUtc);

        Assert.True(resultado);
    }

    [Fact]
    public async Task TieneBingoActivoAsync_ConBingoDeFechaSorteoPasada_DevuelveFalse()
    {
        var organizadorId = Guid.NewGuid();
        var ahoraUtc = DateTime.UtcNow;
        // Bingo.Crear exige fechaSorteoUtc futura respecto de ahoraUtc — se crea válido y luego se
        // fuerza la fecha al pasado directamente sobre el ChangeTracker (acceso vía reflection de
        // EF Core a la propiedad `private init`, mismo mecanismo con el que EF materializa
        // entidades leídas de la base) para simular un bingo cuyo sorteo ya venció.
        var bingo = NuevoBingo(organizadorId, ahoraUtc.AddDays(5), ahoraUtc);

        _context.Bingos.Add(bingo);
        _context.Entry(bingo).Property(nameof(Bingo.FechaSorteoUtc)).CurrentValue = ahoraUtc.AddDays(-1);
        await _context.SaveChangesAsync();

        var resultado = await _repository.TieneBingoActivoAsync(organizadorId, ahoraUtc);

        Assert.False(resultado);
    }

    [Fact]
    public async Task TieneBingoActivoAsync_SinNingunBingoParaEseOrganizador_DevuelveFalse()
    {
        var organizadorId = Guid.NewGuid();
        var ahoraUtc = DateTime.UtcNow;

        var resultado = await _repository.TieneBingoActivoAsync(organizadorId, ahoraUtc);

        Assert.False(resultado);
    }

    [Fact]
    public async Task CrearAsync_ConBingoYCartonesValidos_QuedanPersistidosYRecuperablesConSusNumerosCorrectos()
    {
        var organizadorId = Guid.NewGuid();
        var ahoraUtc = DateTime.UtcNow;
        var bingo = NuevoBingo(organizadorId, ahoraUtc.AddDays(5), ahoraUtc);

        var cartones = new[]
        {
            NuevoCarton(bingo.Id, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10),
            NuevoCarton(bingo.Id, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20),
            NuevoCarton(bingo.Id, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30),
            NuevoCarton(bingo.Id, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40),
            NuevoCarton(bingo.Id, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50),
            NuevoCarton(bingo.Id, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60),
            NuevoCarton(bingo.Id, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70),
        };

        await _repository.CrearAsync(bingo, cartones);

        var optionsNuevo = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        await using var contextoNuevo = new AppDbContext(optionsNuevo);

        var bingoPersistido = await contextoNuevo.Bingos.SingleAsync(b => b.Id == bingo.Id);
        Assert.Equal(bingo.NombreEvento, bingoPersistido.NombreEvento);

        var cartonesPersistidos = await contextoNuevo.Cartones
            .Where(c => c.BingoId == bingo.Id)
            .ToListAsync();

        Assert.Equal(cartones.Length, cartonesPersistidos.Count);

        foreach (var esperado in cartones)
        {
            var recuperado = cartonesPersistidos.Single(c => c.Id == esperado.Id);
            Assert.Equal(esperado.Numeros, recuperado.Numeros);
            Assert.Equal(10, recuperado.Numeros.Count);
        }
    }

    [Fact]
    public async Task InsertarDosCartonesConMismoBingoIdYMismosNumeros_LanzaDbUpdateExceptionPorIndiceUnico()
    {
        var organizadorId = Guid.NewGuid();
        var ahoraUtc = DateTime.UtcNow;
        var bingo = NuevoBingo(organizadorId, ahoraUtc.AddDays(5), ahoraUtc);
        _context.Bingos.Add(bingo);
        await _context.SaveChangesAsync();

        var primerCarton = NuevoCarton(bingo.Id, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10);
        var segundoCartonMismosNumeros = NuevoCarton(bingo.Id, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10);

        _context.Cartones.Add(primerCarton);
        await _context.SaveChangesAsync();

        _context.Cartones.Add(segundoCartonMismosNumeros);

        await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
    }
}
