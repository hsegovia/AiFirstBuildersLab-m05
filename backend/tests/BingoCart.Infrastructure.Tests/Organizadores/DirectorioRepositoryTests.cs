using System;
using System.Linq;
using System.Threading.Tasks;
using BingoCart.Application.Organizadores;
using BingoCart.Application.Organizadores.Dtos;
using BingoCart.Domain.Bingos;
using BingoCart.Infrastructure.Data;
using BingoCart.Infrastructure.Identity;
using BingoCart.Infrastructure.Organizadores;
using Microsoft.EntityFrameworkCore;

namespace BingoCart.Infrastructure.Tests.Organizadores;

/// <summary>
/// Tests de integración de <see cref="DirectorioRepository"/> contra SQL Server real (spec
/// FEAT-005, Block 1) — mismo patrón que <c>BingoRepositoryTests</c>: base propia y descartable
/// (<c>BingoCartTests_DirectorioRepository</c>), migrada al inicio y eliminada en
/// <see cref="DisposeAsync"/> (Rule #0 de testing.instructions.md). A diferencia de
/// <c>BingoRepositoryTests</c> (que usa <c>Guid.NewGuid()</c> sueltos como <c>OrganizadorId</c>,
/// sin fila real de <see cref="ApplicationUser"/>), estos tests SÍ necesitan crear filas
/// <see cref="ApplicationUser"/> reales (mismo <c>Id</c> que <c>Bingo.OrganizadorId</c>) porque
/// <see cref="DirectorioRepository"/> hace un JOIN real contra <c>AppDbContext.Users</c> — sin el
/// <see cref="ApplicationUser"/>, la fila del bingo no aparece en el resultado.
/// </summary>
public sealed class DirectorioRepositoryTests : IAsyncLifetime
{
    // Mismo password de desarrollo local declarado en docker-compose.yml — nunca es un secreto
    // real, solo la credencial del contenedor SQL Server efímero de desarrollo.
    private const string ConnectionString =
        "Server=localhost,14330;Database=BingoCartTests_DirectorioRepository;User Id=sa;" +
        "Password=BingoCart_Dev2026!;TrustServerCertificate=True;Encrypt=True;";

    private AppDbContext _context = null!;
    private IDirectorioRepository _repository = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        _context = new AppDbContext(options);
        await _context.Database.MigrateAsync();

        _repository = new DirectorioRepository(_context);
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

    // Cuit único requerido por el índice único de AppDbContext (`HasIndex(u => u.Cuit).IsUnique()`)
    // — cada organizador de prueba necesita uno distinto para no colisionar entre tests/filas.
    private static ApplicationUser NuevoOrganizador(Guid id, string nombreOrganizacion) => new()
    {
        Id = id,
        UserName = $"{id}@example.com",
        Email = $"{id}@example.com",
        NombreOrganizacion = nombreOrganizacion,
        Cuit = id.ToString("N")[..11],
        Telefono = "+54 11 4444-5555",
    };

    [Fact]
    public async Task ListarActivosAsync_ConDosOrganizadoresConBingoActivo_DevuelveLosDosConCamposCorrectos()
    {
        var ahoraUtc = DateTime.UtcNow;
        var organizadorUno = NuevoOrganizador(Guid.NewGuid(), "Club Uno");
        var organizadorDos = NuevoOrganizador(Guid.NewGuid(), "Club Dos");
        var bingoUno = NuevoBingo(organizadorUno.Id, ahoraUtc.AddDays(5), ahoraUtc);
        var bingoDos = NuevoBingo(organizadorDos.Id, ahoraUtc.AddDays(6), ahoraUtc);

        _context.Users.AddRange(organizadorUno, organizadorDos);
        _context.Bingos.AddRange(bingoUno, bingoDos);
        await _context.SaveChangesAsync();

        var resultado = await _repository.ListarActivosAsync(ahoraUtc, page: 1, pageSize: 20);

        Assert.Equal(2, resultado.Total);
        Assert.Equal(2, resultado.Items.Count);
        Assert.Contains(resultado.Items, i =>
            i.NombreOrganizacion == "Club Uno" &&
            i.NombreEvento == bingoUno.NombreEvento &&
            i.FechaSorteoUtc == bingoUno.FechaSorteoUtc);
        Assert.Contains(resultado.Items, i =>
            i.NombreOrganizacion == "Club Dos" &&
            i.NombreEvento == bingoDos.NombreEvento &&
            i.FechaSorteoUtc == bingoDos.FechaSorteoUtc);
    }

    [Fact]
    public async Task ListarActivosAsync_ConOrganizadorCuyoUnicoBingoTieneFechaSorteoPasada_NoAparece()
    {
        var ahoraUtc = DateTime.UtcNow;
        var organizador = NuevoOrganizador(Guid.NewGuid(), "Club Vencido");
        var bingo = NuevoBingo(organizador.Id, ahoraUtc.AddDays(5), ahoraUtc);

        _context.Users.Add(organizador);
        _context.Bingos.Add(bingo);
        // `Bingo.Crear` exige `fechaSorteoUtc` futura respecto de `ahoraUtc` — se crea válido y
        // luego se fuerza la fecha al pasado directamente sobre el ChangeTracker (mismo mecanismo
        // que BingoRepositoryTests) para simular un bingo cuyo sorteo ya venció.
        _context.Entry(bingo).Property(nameof(Bingo.FechaSorteoUtc)).CurrentValue = ahoraUtc.AddDays(-1);
        await _context.SaveChangesAsync();

        var resultado = await _repository.ListarActivosAsync(ahoraUtc, page: 1, pageSize: 20);

        Assert.Empty(resultado.Items);
        Assert.Equal(0, resultado.Total);
    }

    [Fact]
    public async Task ListarActivosAsync_SinNingunOrganizadorConBingoActivo_DevuelveVacioYTotalCero()
    {
        var ahoraUtc = DateTime.UtcNow;

        var resultado = await _repository.ListarActivosAsync(ahoraUtc, page: 1, pageSize: 20);

        Assert.Empty(resultado.Items);
        Assert.Equal(0, resultado.Total);
    }

    [Fact]
    public async Task ListarActivosAsync_ConBingosDeDistintaFechaSorteo_DevuelveOrdenAscendente()
    {
        var ahoraUtc = DateTime.UtcNow;
        var organizadorMasLejano = NuevoOrganizador(Guid.NewGuid(), "Club Lejano");
        var organizadorMasProximo = NuevoOrganizador(Guid.NewGuid(), "Club Próximo");
        var bingoLejano = NuevoBingo(organizadorMasLejano.Id, ahoraUtc.AddDays(30), ahoraUtc);
        var bingoProximo = NuevoBingo(organizadorMasProximo.Id, ahoraUtc.AddDays(1), ahoraUtc);

        _context.Users.AddRange(organizadorMasLejano, organizadorMasProximo);
        _context.Bingos.AddRange(bingoLejano, bingoProximo);
        await _context.SaveChangesAsync();

        var resultado = await _repository.ListarActivosAsync(ahoraUtc, page: 1, pageSize: 20);

        Assert.Equal(bingoProximo.FechaSorteoUtc, resultado.Items[0].FechaSorteoUtc);
        Assert.Equal(bingoLejano.FechaSorteoUtc, resultado.Items[1].FechaSorteoUtc);
    }

    [Fact]
    public async Task ListarActivosAsync_ConPageDosPageSizeDosYTresOrganizadoresActivos_DevuelveElRestante()
    {
        var ahoraUtc = DateTime.UtcNow;
        var organizadorUno = NuevoOrganizador(Guid.NewGuid(), "Club Uno");
        var organizadorDos = NuevoOrganizador(Guid.NewGuid(), "Club Dos");
        var organizadorTres = NuevoOrganizador(Guid.NewGuid(), "Club Tres");
        var bingoUno = NuevoBingo(organizadorUno.Id, ahoraUtc.AddDays(1), ahoraUtc);
        var bingoDos = NuevoBingo(organizadorDos.Id, ahoraUtc.AddDays(2), ahoraUtc);
        var bingoTres = NuevoBingo(organizadorTres.Id, ahoraUtc.AddDays(3), ahoraUtc);

        _context.Users.AddRange(organizadorUno, organizadorDos, organizadorTres);
        _context.Bingos.AddRange(bingoUno, bingoDos, bingoTres);
        await _context.SaveChangesAsync();

        var resultado = await _repository.ListarActivosAsync(ahoraUtc, page: 2, pageSize: 2);

        Assert.Single(resultado.Items);
        Assert.Equal(3, resultado.Total);
        Assert.Equal(bingoTres.FechaSorteoUtc, resultado.Items[0].FechaSorteoUtc);
    }

    [Fact]
    public async Task ListarActivosAsync_ProyeccionResultante_NuncaExponeCamposMasAllaDeLosTresDelDto()
    {
        // Inspección estructural (no de valores): DirectorioOrganizadorItem solo declara
        // NombreOrganizacion, NombreEvento y FechaSorteoUtc — es estructuralmente imposible que el
        // repositorio devuelva CUIT/mail/teléfono, sin importar qué datos tenga el organizador
        // sembrado. Valida NFR-02/AC-08 a nivel de infraestructura.
        var ahoraUtc = DateTime.UtcNow;
        var organizador = NuevoOrganizador(Guid.NewGuid(), "Club Con Datos Sensibles");
        var bingo = NuevoBingo(organizador.Id, ahoraUtc.AddDays(1), ahoraUtc);

        _context.Users.Add(organizador);
        _context.Bingos.Add(bingo);
        await _context.SaveChangesAsync();

        var resultado = await _repository.ListarActivosAsync(ahoraUtc, page: 1, pageSize: 20);

        var propiedades = typeof(DirectorioOrganizadorItem)
            .GetProperties()
            .Select(p => p.Name)
            .ToList();

        Assert.Equal(
            new[] { "NombreOrganizacion", "NombreEvento", "FechaSorteoUtc" },
            propiedades);
        Assert.Single(resultado.Items);
    }
}
