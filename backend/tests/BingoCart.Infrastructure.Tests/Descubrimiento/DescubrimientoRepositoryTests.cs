using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BingoCart.Application.Descubrimiento;
using BingoCart.Domain.Bingos;
using BingoCart.Infrastructure.Data;
using BingoCart.Infrastructure.Descubrimiento;
using BingoCart.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace BingoCart.Infrastructure.Tests.Descubrimiento;

/// <summary>
/// Tests de integración de <see cref="DescubrimientoRepository"/> contra SQL Server real (spec
/// FEAT-008a, Block 1) — mismo patrón que <c>DirectorioRepositoryTests</c>/<c>BingoRepositoryTests</c>:
/// base propia y descartable (<c>BingoCartTests_DescubrimientoRepository</c>), migrada al inicio y
/// eliminada en <see cref="DisposeAsync"/> (Rule #0 de testing.instructions.md). Necesita filas
/// <see cref="ApplicationUser"/> reales (mismo <c>Id</c> que <c>Bingo.OrganizadorId</c>) porque
/// <c>ObtenerResumenBingosAsync</c> hace un JOIN real contra <c>AppDbContext.Users</c>, igual que
/// <c>DirectorioRepository</c>.
/// </summary>
public sealed class DescubrimientoRepositoryTests : IAsyncLifetime
{
    // Mismo password de desarrollo local declarado en docker-compose.yml — nunca es un secreto
    // real, solo la credencial del contenedor SQL Server efímero de desarrollo.
    private const string ConnectionString =
        "Server=localhost,14330;Database=BingoCartTests_DescubrimientoRepository;User Id=sa;" +
        "Password=BingoCart_Dev2026!;TrustServerCertificate=True;Encrypt=True;";

    private AppDbContext _context = null!;
    private IDescubrimientoRepository _repository = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        _context = new AppDbContext(options);
        await _context.Database.MigrateAsync();

        _repository = new DescubrimientoRepository(_context);
    }

    public async Task DisposeAsync()
    {
        await _context.Database.EnsureDeletedAsync();
        await _context.DisposeAsync();
    }

    private static Bingo NuevoBingo(Guid organizadorId, DateTime fechaSorteoUtc, DateTime ahoraUtc, decimal costoPorCarton = 100m) =>
        Bingo.Crear(
            nombreEvento: "Bingo de prueba",
            fechaSorteoUtc: fechaSorteoUtc,
            cantidadCartones: 5000,
            costoPorCarton: costoPorCarton,
            organizadorId: organizadorId,
            ahoraUtc: ahoraUtc);

    private static Carton NuevoCarton(Guid bingoId, params int[] numeros) => Carton.Crear(bingoId, numeros);

    // Genera `cantidad` cartones únicos y válidos para `bingoId`, sin colisionar entre sí: usa una
    // ventana deslizante de 10 números dentro del rango 1-90 (1-10, 2-11, 3-12, ...) — cada
    // desplazamiento produce un conjunto de números distinto de los anteriores, soporta hasta 81
    // cartones sin repetir el índice único (BingoId, NumerosSerializados).
    private static List<Carton> NuevosCartones(Guid bingoId, int cantidad)
    {
        var cartones = new List<Carton>();
        for (var i = 0; i < cantidad; i++)
        {
            var numeros = Enumerable.Range(1 + i, 10).ToArray();
            cartones.Add(Carton.Crear(bingoId, numeros));
        }

        return cartones;
    }

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
    public async Task ObtenerAleatoriosGlobalAsync_ConDosBingosActivosYUnoVencido_NuncaDevuelveCartonesDelVencido()
    {
        var ahoraUtc = DateTime.UtcNow;
        var organizadorUno = NuevoOrganizador(Guid.NewGuid(), "Club Uno");
        var organizadorDos = NuevoOrganizador(Guid.NewGuid(), "Club Dos");
        var organizadorVencido = NuevoOrganizador(Guid.NewGuid(), "Club Vencido");
        var bingoUno = NuevoBingo(organizadorUno.Id, ahoraUtc.AddDays(5), ahoraUtc);
        var bingoDos = NuevoBingo(organizadorDos.Id, ahoraUtc.AddDays(6), ahoraUtc);
        var bingoVencido = NuevoBingo(organizadorVencido.Id, ahoraUtc.AddDays(7), ahoraUtc);

        _context.Users.AddRange(organizadorUno, organizadorDos, organizadorVencido);
        _context.Bingos.AddRange(bingoUno, bingoDos, bingoVencido);
        _context.Cartones.AddRange(NuevosCartones(bingoUno.Id, 3));
        _context.Cartones.AddRange(NuevosCartones(bingoDos.Id, 3));
        _context.Cartones.AddRange(NuevosCartones(bingoVencido.Id, 3));
        await _context.SaveChangesAsync();

        _context.Entry(bingoVencido).Property(nameof(Bingo.FechaSorteoUtc)).CurrentValue = ahoraUtc.AddDays(-1);
        await _context.SaveChangesAsync();

        var resultado = await _repository.ObtenerAleatoriosGlobalAsync(ahoraUtc, cantidad: 5);

        Assert.All(resultado, c => Assert.True(c.BingoId == bingoUno.Id || c.BingoId == bingoDos.Id));
        Assert.DoesNotContain(resultado, c => c.BingoId == bingoVencido.Id);
    }

    [Fact]
    public async Task ObtenerAleatoriosGlobalAsync_ConMenosDeCincoCartonesElegibles_DevuelveTodosLosDisponibles()
    {
        var ahoraUtc = DateTime.UtcNow;
        var organizador = NuevoOrganizador(Guid.NewGuid(), "Club Chico");
        var bingo = NuevoBingo(organizador.Id, ahoraUtc.AddDays(5), ahoraUtc);

        _context.Users.Add(organizador);
        _context.Bingos.Add(bingo);
        _context.Cartones.AddRange(NuevosCartones(bingo.Id, 3));
        await _context.SaveChangesAsync();

        var resultado = await _repository.ObtenerAleatoriosGlobalAsync(ahoraUtc, cantidad: 5);

        Assert.Equal(3, resultado.Count);
    }

    [Fact]
    public async Task ObtenerAleatoriosGlobalAsync_SinNingunBingoActivo_DevuelveListaVacia()
    {
        var ahoraUtc = DateTime.UtcNow;

        var resultado = await _repository.ObtenerAleatoriosGlobalAsync(ahoraUtc, cantidad: 5);

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task ObtenerAleatoriosGlobalAsync_ConSuficientesCartonesElegibles_LosCincoDevueltosSonDistintos()
    {
        var ahoraUtc = DateTime.UtcNow;
        var organizador = NuevoOrganizador(Guid.NewGuid(), "Club Grande");
        var bingo = NuevoBingo(organizador.Id, ahoraUtc.AddDays(5), ahoraUtc);

        _context.Users.Add(organizador);
        _context.Bingos.Add(bingo);
        _context.Cartones.AddRange(NuevosCartones(bingo.Id, 9));
        await _context.SaveChangesAsync();

        var resultado = await _repository.ObtenerAleatoriosGlobalAsync(ahoraUtc, cantidad: 5);

        Assert.Equal(5, resultado.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public async Task ExisteOrganizadorAsync_ConOrganizadorRealYConGuidAleatorio_DevuelveTrueYFalseRespectivamente()
    {
        var organizador = NuevoOrganizador(Guid.NewGuid(), "Club Existente");
        _context.Users.Add(organizador);
        await _context.SaveChangesAsync();

        var existente = await _repository.ExisteOrganizadorAsync(organizador.Id);
        var inexistente = await _repository.ExisteOrganizadorAsync(Guid.NewGuid());

        Assert.True(existente);
        Assert.False(inexistente);
    }

    [Fact]
    public async Task ObtenerBingoActivoDeOrganizadorAsync_ConBingoActivoYConSorteoPasado_DevuelveIdYNullRespectivamente()
    {
        var ahoraUtc = DateTime.UtcNow;
        var organizadorConActivo = NuevoOrganizador(Guid.NewGuid(), "Club Activo");
        var organizadorVencido = NuevoOrganizador(Guid.NewGuid(), "Club Vencido");
        var bingoActivo = NuevoBingo(organizadorConActivo.Id, ahoraUtc.AddDays(5), ahoraUtc);
        var bingoVencido = NuevoBingo(organizadorVencido.Id, ahoraUtc.AddDays(5), ahoraUtc);

        _context.Users.AddRange(organizadorConActivo, organizadorVencido);
        _context.Bingos.AddRange(bingoActivo, bingoVencido);
        await _context.SaveChangesAsync();

        _context.Entry(bingoVencido).Property(nameof(Bingo.FechaSorteoUtc)).CurrentValue = ahoraUtc.AddDays(-1);
        await _context.SaveChangesAsync();

        var resultadoActivo = await _repository.ObtenerBingoActivoDeOrganizadorAsync(organizadorConActivo.Id, ahoraUtc);
        var resultadoVencido = await _repository.ObtenerBingoActivoDeOrganizadorAsync(organizadorVencido.Id, ahoraUtc);

        Assert.Equal(bingoActivo.Id, resultadoActivo);
        Assert.Null(resultadoVencido);
    }

    [Fact]
    public async Task ObtenerAleatoriosDeBingoAsync_ConBingoConMasDeCincoCartones_DevuelveExactamenteCincoDelBingoCorrecto()
    {
        var ahoraUtc = DateTime.UtcNow;
        var organizador = NuevoOrganizador(Guid.NewGuid(), "Club Con Muchos Cartones");
        var otroOrganizador = NuevoOrganizador(Guid.NewGuid(), "Otro Club");
        var bingo = NuevoBingo(organizador.Id, ahoraUtc.AddDays(5), ahoraUtc);
        var otroBingo = NuevoBingo(otroOrganizador.Id, ahoraUtc.AddDays(5), ahoraUtc);

        _context.Users.AddRange(organizador, otroOrganizador);
        _context.Bingos.AddRange(bingo, otroBingo);
        _context.Cartones.AddRange(NuevosCartones(bingo.Id, 9));
        _context.Cartones.AddRange(NuevosCartones(otroBingo.Id, 3));
        await _context.SaveChangesAsync();

        var resultado = await _repository.ObtenerAleatoriosDeBingoAsync(bingo.Id, cantidad: 5);

        Assert.Equal(5, resultado.Count);
        Assert.All(resultado, c => Assert.Equal(bingo.Id, c.BingoId));
    }

    [Fact]
    public async Task ObtenerAleatoriosDeBingoAsync_ConBingoConMenosDeCincoCartones_DevuelveTodosSinError()
    {
        var ahoraUtc = DateTime.UtcNow;
        var organizador = NuevoOrganizador(Guid.NewGuid(), "Club Chico");
        var bingo = NuevoBingo(organizador.Id, ahoraUtc.AddDays(5), ahoraUtc);

        _context.Users.Add(organizador);
        _context.Bingos.Add(bingo);
        _context.Cartones.AddRange(NuevosCartones(bingo.Id, 2));
        await _context.SaveChangesAsync();

        var resultado = await _repository.ObtenerAleatoriosDeBingoAsync(bingo.Id, cantidad: 5);

        Assert.Equal(2, resultado.Count);
    }

    [Fact]
    public async Task ObtenerResumenBingosAsync_ConDosBingoIdReales_DevuelveCamposCorrectosParaAmbos()
    {
        var ahoraUtc = DateTime.UtcNow;
        var organizadorUno = NuevoOrganizador(Guid.NewGuid(), "Club Uno");
        var organizadorDos = NuevoOrganizador(Guid.NewGuid(), "Club Dos");
        var bingoUno = NuevoBingo(organizadorUno.Id, ahoraUtc.AddDays(5), ahoraUtc, costoPorCarton: 150m);
        var bingoDos = NuevoBingo(organizadorDos.Id, ahoraUtc.AddDays(6), ahoraUtc, costoPorCarton: 200m);

        _context.Users.AddRange(organizadorUno, organizadorDos);
        _context.Bingos.AddRange(bingoUno, bingoDos);
        await _context.SaveChangesAsync();

        var resultado = await _repository.ObtenerResumenBingosAsync(new[] { bingoUno.Id, bingoDos.Id });

        Assert.Equal(2, resultado.Count);
        Assert.Contains(resultado, r =>
            r.Id == bingoUno.Id &&
            r.NombreOrganizacion == "Club Uno" &&
            r.NombreEvento == bingoUno.NombreEvento &&
            r.CostoPorCarton == 150m &&
            r.FechaSorteoUtc == bingoUno.FechaSorteoUtc);
        Assert.Contains(resultado, r =>
            r.Id == bingoDos.Id &&
            r.NombreOrganizacion == "Club Dos" &&
            r.NombreEvento == bingoDos.NombreEvento &&
            r.CostoPorCarton == 200m &&
            r.FechaSorteoUtc == bingoDos.FechaSorteoUtc);
    }

    [Fact]
    public async Task ObtenerAleatoriosGlobalAsync_InvocadoCincoVecesContraUnPoolDeVeinteCartones_NoDevuelveSiempreElMismoSubconjunto()
    {
        // Test estadístico, no determinístico por naturaleza (ver "Required tests" del spec, AC-06):
        // con C(20,5) = 15504 combinaciones posibles, la probabilidad de que 5 corridas de
        // `ORDER BY NEWID()` devuelvan exactamente el mismo subconjunto es despreciable. No afirma
        // que el mecanismo nunca pueda repetir por azar, solo que no está cacheado ni es
        // determinístico.
        var ahoraUtc = DateTime.UtcNow;
        var organizador = NuevoOrganizador(Guid.NewGuid(), "Club Pool Grande");
        var bingo = NuevoBingo(organizador.Id, ahoraUtc.AddDays(5), ahoraUtc);

        _context.Users.Add(organizador);
        _context.Bingos.Add(bingo);
        _context.Cartones.AddRange(NuevosCartones(bingo.Id, 20));
        await _context.SaveChangesAsync();

        var idsAcumulados = new HashSet<Guid>();
        for (var i = 0; i < 5; i++)
        {
            var resultado = await _repository.ObtenerAleatoriosGlobalAsync(ahoraUtc, cantidad: 5);
            foreach (var carton in resultado)
            {
                idsAcumulados.Add(carton.Id);
            }
        }

        Assert.True(idsAcumulados.Count > 5, $"Se esperaban más de 5 cartones distintos entre las 5 corridas, se obtuvieron {idsAcumulados.Count}.");
    }
}
