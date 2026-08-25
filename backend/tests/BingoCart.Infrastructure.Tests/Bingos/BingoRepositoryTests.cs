using System;
using System.Linq;
using System.Threading.Tasks;
using BingoCart.Application.Bingos;
using BingoCart.Domain.Bingos;
using BingoCart.Domain.Compras;
using BingoCart.Infrastructure.Bingos;
using BingoCart.Infrastructure.Data;
using BingoCart.Infrastructure.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

// CompraCarton (Infrastructure.Data): fila de persistencia pura de CompraCartones, sembrada
// directamente en varios tests de esta clase para simular "cartón ya vendido" sin necesitar
// CompraRepository/Compra completos (spec FEAT-009a, Block 1).

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

    private static Bingo NuevoBingo(Guid organizadorId, DateTime fechaSorteoUtc, DateTime ahoraUtc, decimal costoPorCarton = 100m) =>
        Bingo.Crear(
            nombreEvento: "Bingo de prueba",
            fechaSorteoUtc: fechaSorteoUtc,
            cantidadCartones: 10,
            costoPorCarton: costoPorCarton,
            organizadorId: organizadorId,
            ahoraUtc: ahoraUtc);

    private static Carton NuevoCarton(Guid bingoId, int numeroCorrelativo, params int[] numeros) =>
        Carton.Crear(bingoId, numeros, numeroCorrelativo);

    // ObtenerParaCarritoAsync (spec FEAT-008b, Block 2) hace un JOIN real contra AppDbContext.Users
    // para NombreOrganizacion — a diferencia del resto de tests de esta clase (Bingo.OrganizadorId
    // es una FK lógica, sin fila real de ApplicationUser necesaria), este helper crea una fila real.
    // Cuit único requerido por el índice único de AppDbContext (`HasIndex(u => u.Cuit).IsUnique()`).
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
            NuevoCarton(bingo.Id, numeroCorrelativo: 1, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10),
            NuevoCarton(bingo.Id, numeroCorrelativo: 2, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20),
            NuevoCarton(bingo.Id, numeroCorrelativo: 3, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30),
            NuevoCarton(bingo.Id, numeroCorrelativo: 4, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40),
            NuevoCarton(bingo.Id, numeroCorrelativo: 5, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50),
            NuevoCarton(bingo.Id, numeroCorrelativo: 6, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60),
            NuevoCarton(bingo.Id, numeroCorrelativo: 7, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70),
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

    // Los dos cartones se siembran con correlativos DISTINTOS a propósito (FEAT-009d, R-09): con el
    // mismo correlativo, el INSERT violaría también IX_Cartones_BingoId_NumeroCorrelativo y el test
    // pasaría por el índice equivocado, dejando de cubrir lo que dice cubrir.
    [Fact]
    public async Task InsertarDosCartonesConMismoBingoIdYMismosNumeros_LanzaDbUpdateExceptionPorIndiceUnico()
    {
        var organizadorId = Guid.NewGuid();
        var ahoraUtc = DateTime.UtcNow;
        var bingo = NuevoBingo(organizadorId, ahoraUtc.AddDays(5), ahoraUtc);
        _context.Bingos.Add(bingo);
        await _context.SaveChangesAsync();

        var primerCarton = NuevoCarton(bingo.Id, numeroCorrelativo: 1, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10);
        var segundoCartonMismosNumeros = NuevoCarton(bingo.Id, numeroCorrelativo: 2, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10);

        _context.Cartones.Add(primerCarton);
        await _context.SaveChangesAsync();

        _context.Cartones.Add(segundoCartonMismosNumeros);

        var excepcion = await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());

        // ThrowsAsync garantiza el tipo de la excepción EXTERNA, no que traiga inner: se tipa con
        // IsType (que devuelve la instancia) en vez de con `!`, para que una DbUpdateException sin
        // inner falle con un mensaje legible y no con un NullReferenceException. El 2601 es el
        // error de SQL Server por violación de índice único, y el nombre distingue CUÁL.
        var sqlException = Assert.IsType<SqlException>(excepcion.InnerException);

        Assert.Equal(2601, sqlException.Number);
        Assert.Contains("IX_Cartones_BingoId_NumerosSerializados", sqlException.Message);
    }

    [Fact]
    public async Task Insertar_DosCartonesConMismoBingoYCorrelativo_LanzaDbUpdateException()
    {
        var organizadorId = Guid.NewGuid();
        var ahoraUtc = DateTime.UtcNow;
        var bingo = NuevoBingo(organizadorId, ahoraUtc.AddDays(5), ahoraUtc);
        _context.Bingos.Add(bingo);
        await _context.SaveChangesAsync();

        var primerCarton = NuevoCarton(bingo.Id, numeroCorrelativo: 1, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10);
        var segundoCartonMismoCorrelativo = NuevoCarton(bingo.Id, numeroCorrelativo: 1, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20);

        _context.Cartones.Add(primerCarton);
        await _context.SaveChangesAsync();

        _context.Cartones.Add(segundoCartonMismoCorrelativo);

        var excepcion = await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());

        // ThrowsAsync garantiza el tipo de la excepción EXTERNA, no que traiga inner: se tipa con
        // IsType (que devuelve la instancia) en vez de con `!`, para que una DbUpdateException sin
        // inner falle con un mensaje legible y no con un NullReferenceException. El 2601 es el
        // error de SQL Server por violación de índice único, y el nombre distingue CUÁL.
        var sqlException = Assert.IsType<SqlException>(excepcion.InnerException);

        Assert.Equal(2601, sqlException.Number);
        Assert.Contains("IX_Cartones_BingoId_NumeroCorrelativo", sqlException.Message);
    }

    [Fact]
    public async Task ListarPorOrganizadorAsync_ConTresBingosYPageSizeDos_DevuelveDosBingosYTotalTres()
    {
        var organizadorId = Guid.NewGuid();
        var ahoraUtc = DateTime.UtcNow;
        _context.Bingos.AddRange(
            NuevoBingo(organizadorId, ahoraUtc.AddDays(5), ahoraUtc),
            NuevoBingo(organizadorId, ahoraUtc.AddDays(6), ahoraUtc.AddMinutes(1)),
            NuevoBingo(organizadorId, ahoraUtc.AddDays(7), ahoraUtc.AddMinutes(2)));
        await _context.SaveChangesAsync();

        var resultado = await _repository.ListarPorOrganizadorAsync(organizadorId, page: 1, pageSize: 2);

        Assert.Equal(2, resultado.Bingos.Count);
        Assert.Equal(3, resultado.Total);
    }

    [Fact]
    public async Task ListarPorOrganizadorAsync_ConBingosDeDistintaFechaCreacion_DevuelveOrdenDescendente()
    {
        var organizadorId = Guid.NewGuid();
        var ahoraUtc = DateTime.UtcNow;
        var masAntiguo = NuevoBingo(organizadorId, ahoraUtc.AddDays(5), ahoraUtc);
        var masReciente = NuevoBingo(organizadorId, ahoraUtc.AddDays(6), ahoraUtc.AddMinutes(10));
        _context.Bingos.AddRange(masAntiguo, masReciente);
        await _context.SaveChangesAsync();

        var resultado = await _repository.ListarPorOrganizadorAsync(organizadorId, page: 1, pageSize: 10);

        Assert.Equal(masReciente.Id, resultado.Bingos[0].Id);
        Assert.Equal(masAntiguo.Id, resultado.Bingos[1].Id);
    }

    [Fact]
    public async Task ListarPorOrganizadorAsync_ConOrganizadorSinBingos_DevuelveVacioYTotalCero()
    {
        var organizadorId = Guid.NewGuid();

        var resultado = await _repository.ListarPorOrganizadorAsync(organizadorId, page: 1, pageSize: 20);

        Assert.Empty(resultado.Bingos);
        Assert.Equal(0, resultado.Total);
    }

    [Fact]
    public async Task ListarPorOrganizadorAsync_ConBingosDeOtroOrganizador_NoLosIncluyeEnElResultado()
    {
        var organizadorId = Guid.NewGuid();
        var otroOrganizadorId = Guid.NewGuid();
        var ahoraUtc = DateTime.UtcNow;
        _context.Bingos.Add(NuevoBingo(otroOrganizadorId, ahoraUtc.AddDays(5), ahoraUtc));
        await _context.SaveChangesAsync();

        var resultado = await _repository.ListarPorOrganizadorAsync(organizadorId, page: 1, pageSize: 20);

        Assert.Empty(resultado.Bingos);
        Assert.Equal(0, resultado.Total);
    }

    [Fact]
    public async Task ListarPorOrganizadorAsync_ConPageDosPageSizeDosYTresBingos_DevuelveElBingoRestante()
    {
        var organizadorId = Guid.NewGuid();
        var ahoraUtc = DateTime.UtcNow;
        var primero = NuevoBingo(organizadorId, ahoraUtc.AddDays(5), ahoraUtc);
        var segundo = NuevoBingo(organizadorId, ahoraUtc.AddDays(6), ahoraUtc.AddMinutes(1));
        var tercero = NuevoBingo(organizadorId, ahoraUtc.AddDays(7), ahoraUtc.AddMinutes(2));
        _context.Bingos.AddRange(primero, segundo, tercero);
        await _context.SaveChangesAsync();

        var resultado = await _repository.ListarPorOrganizadorAsync(organizadorId, page: 2, pageSize: 2);

        Assert.Single(resultado.Bingos);
        Assert.Equal(primero.Id, resultado.Bingos[0].Id);
        Assert.Equal(3, resultado.Total);
    }

    [Fact]
    public async Task ObtenerPorIdAsync_ConIdExistente_DevuelveElBingoCorrecto()
    {
        var organizadorId = Guid.NewGuid();
        var ahoraUtc = DateTime.UtcNow;
        var bingo = NuevoBingo(organizadorId, ahoraUtc.AddDays(5), ahoraUtc);
        _context.Bingos.Add(bingo);
        await _context.SaveChangesAsync();

        var resultado = await _repository.ObtenerPorIdAsync(bingo.Id);

        Assert.NotNull(resultado);
        Assert.Equal(bingo.Id, resultado!.Id);
        Assert.Equal(bingo.OrganizadorId, resultado.OrganizadorId);
        Assert.Equal(bingo.NombreEvento, resultado.NombreEvento);
        Assert.Equal(bingo.FechaSorteoUtc, resultado.FechaSorteoUtc);
        Assert.Equal(bingo.CantidadCartones, resultado.CantidadCartones);
        Assert.Equal(bingo.CostoPorCarton, resultado.CostoPorCarton);
    }

    [Fact]
    public async Task ObtenerPorIdAsync_ConIdInexistente_DevuelveNull()
    {
        var resultado = await _repository.ObtenerPorIdAsync(Guid.NewGuid());

        Assert.Null(resultado);
    }

    [Fact]
    public async Task EliminarAsync_SobreUnBingoConCartones_NiElBingoNiSusCartonesQuedanEnLaBase()
    {
        var organizadorId = Guid.NewGuid();
        var ahoraUtc = DateTime.UtcNow;
        var bingo = NuevoBingo(organizadorId, ahoraUtc.AddDays(5), ahoraUtc);
        var cartones = new[]
        {
            NuevoCarton(bingo.Id, numeroCorrelativo: 1, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10),
            NuevoCarton(bingo.Id, numeroCorrelativo: 2, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20),
        };
        await _repository.CrearAsync(bingo, cartones);

        await _repository.EliminarAsync(bingo);

        var bingoRestante = await _context.Bingos.SingleOrDefaultAsync(b => b.Id == bingo.Id);
        var cartonesRestantes = await _context.Cartones.Where(c => c.BingoId == bingo.Id).ToListAsync();
        Assert.Null(bingoRestante);
        Assert.Empty(cartonesRestantes);
    }

    // Reemplaza el test "siempre false" (spec FEAT-009a, Block 1 — reemplaza
    // BingoRepositoryTests.cs:312-321 según impact scan): TieneComprasRegistradasAsync ahora es una
    // implementación real (EXISTS contra CompraCartones join Cartones por BingoId).
    [Fact]
    public async Task TieneComprasRegistradasAsync_ConBingoSinNingunaCompra_DevuelveFalse()
    {
        var organizadorId = Guid.NewGuid();
        var ahoraUtc = DateTime.UtcNow;
        var bingo = NuevoBingo(organizadorId, ahoraUtc.AddDays(5), ahoraUtc);
        _context.Bingos.Add(bingo);
        await _context.SaveChangesAsync();

        var resultado = await _repository.TieneComprasRegistradasAsync(bingo.Id);

        Assert.False(resultado);
    }

    [Fact]
    public async Task TieneComprasRegistradasAsync_ConBingoQueTieneAlMenosUnaFilaEnCompraCartones_DevuelveTrue()
    {
        var organizadorId = Guid.NewGuid();
        var ahoraUtc = DateTime.UtcNow;
        var bingo = NuevoBingo(organizadorId, ahoraUtc.AddDays(5), ahoraUtc);
        var carton = NuevoCarton(bingo.Id, numeroCorrelativo: 1, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10);
        _context.Bingos.Add(bingo);
        _context.Cartones.Add(carton);
        await _context.SaveChangesAsync();

        var compra = SembrarCompraPendiente(organizadorId, Guid.NewGuid());
        _context.Compras.Add(compra);
        _context.CompraCartones.Add(new CompraCarton { CompraId = compra.Id, CartonId = carton.Id, PrecioUnitario = 100m });
        await _context.SaveChangesAsync();

        var resultado = await _repository.TieneComprasRegistradasAsync(bingo.Id);

        Assert.True(resultado);
    }

    [Fact]
    public async Task ObtenerParaConfirmarCompraAsync_ConDosCartonIdDeOrganizadoresDistintos_DevuelveDatosCorrectosParaAmbosSinMezclarlos()
    {
        var ahoraUtc = DateTime.UtcNow;
        var organizadorUno = NuevoOrganizador(Guid.NewGuid(), "Club Confirmar Uno");
        var organizadorDos = NuevoOrganizador(Guid.NewGuid(), "Club Confirmar Dos");
        var bingoUno = NuevoBingo(organizadorUno.Id, ahoraUtc.AddDays(5), ahoraUtc);
        var bingoDos = NuevoBingo(organizadorDos.Id, ahoraUtc.AddDays(6), ahoraUtc);
        var cartonUno = NuevoCarton(bingoUno.Id, numeroCorrelativo: 1, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10);
        var cartonDos = NuevoCarton(bingoDos.Id, numeroCorrelativo: 1, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20);

        _context.Users.AddRange(organizadorUno, organizadorDos);
        _context.Bingos.AddRange(bingoUno, bingoDos);
        _context.Cartones.AddRange(cartonUno, cartonDos);
        await _context.SaveChangesAsync();

        var resultado = await _repository.ObtenerParaConfirmarCompraAsync(new[] { cartonUno.Id, cartonDos.Id });

        Assert.Equal(2, resultado.Count);
        Assert.Contains(resultado, r =>
            r.CartonId == cartonUno.Id &&
            r.BingoId == bingoUno.Id &&
            r.OrganizadorId == organizadorUno.Id &&
            r.NombreOrganizacion == "Club Confirmar Uno" &&
            r.NombreEvento == bingoUno.NombreEvento);
        Assert.Contains(resultado, r =>
            r.CartonId == cartonDos.Id &&
            r.BingoId == bingoDos.Id &&
            r.OrganizadorId == organizadorDos.Id &&
            r.NombreOrganizacion == "Club Confirmar Dos" &&
            r.NombreEvento == bingoDos.NombreEvento);
    }

    [Fact]
    public async Task ObtenerParaCarritoAsync_ConCartonQueYaTieneUnaFilaEnCompraCartones_DevuelveNullAunqueSuBingoSigaActivo()
    {
        var ahoraUtc = DateTime.UtcNow;
        var organizador = NuevoOrganizador(Guid.NewGuid(), "Club Ya Vendido");
        var bingo = NuevoBingo(organizador.Id, ahoraUtc.AddDays(5), ahoraUtc, costoPorCarton: 120m);
        var carton = NuevoCarton(bingo.Id, numeroCorrelativo: 1, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10);

        _context.Users.Add(organizador);
        _context.Bingos.Add(bingo);
        _context.Cartones.Add(carton);
        await _context.SaveChangesAsync();

        var compra = SembrarCompraPendiente(organizador.Id, Guid.NewGuid());
        _context.Compras.Add(compra);
        _context.CompraCartones.Add(new CompraCarton { CompraId = compra.Id, CartonId = carton.Id, PrecioUnitario = 120m });
        await _context.SaveChangesAsync();

        var resultado = await _repository.ObtenerParaCarritoAsync(carton.Id, ahoraUtc);

        Assert.Null(resultado);
    }

    // Helper: fila mínima de `Compras` sembrada directamente (sin pasar por CompraRepository) para
    // simular "ya hay una compra" en tests que solo necesitan la FK, no el flujo completo.
    private static Compra SembrarCompraPendiente(Guid organizadorId, Guid compradorId) =>
        Compra.Crear(
            organizadorId,
            compradorId,
            Guid.NewGuid(),
            new[] { new ItemCompra(Guid.NewGuid(), 100m) },
            MedioPago.Efectivo,
            DateTime.UtcNow);

    // Spec FEAT-009c, Block 3: mismo helper, pero ya transicionada a Cancelado — usada por los tests
    // nuevos que validan que TieneComprasRegistradasAsync/ObtenerParaCarritoAsync excluyen
    // CompraCartones cuya Compra está Cancelada.
    private static Compra SembrarCompraCancelada(Guid organizadorId, Guid compradorId)
    {
        var compra = Compra.Crear(
            organizadorId,
            compradorId,
            Guid.NewGuid(),
            new[] { new ItemCompra(Guid.NewGuid(), 100m) },
            MedioPago.Efectivo,
            DateTime.UtcNow);
        compra.Cancelar();
        return compra;
    }

    [Fact]
    public async Task ObtenerParaCarritoAsync_ConCartonDeBingoActivoConIdInexistenteYConBingoVencido_DevuelveDatosONullSegunCorresponda()
    {
        var ahoraUtc = DateTime.UtcNow;
        var organizadorActivo = NuevoOrganizador(Guid.NewGuid(), "Club Activo Carrito");
        var organizadorVencido = NuevoOrganizador(Guid.NewGuid(), "Club Vencido Carrito");
        var bingoActivo = NuevoBingo(organizadorActivo.Id, ahoraUtc.AddDays(5), ahoraUtc, costoPorCarton: 150m);
        var bingoVencido = NuevoBingo(organizadorVencido.Id, ahoraUtc.AddDays(5), ahoraUtc);

        _context.Users.AddRange(organizadorActivo, organizadorVencido);
        _context.Bingos.AddRange(bingoActivo, bingoVencido);
        var cartonActivo = NuevoCarton(bingoActivo.Id, numeroCorrelativo: 1, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10);
        var cartonVencido = NuevoCarton(bingoVencido.Id, numeroCorrelativo: 1, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20);
        _context.Cartones.AddRange(cartonActivo, cartonVencido);
        await _context.SaveChangesAsync();

        _context.Entry(bingoVencido).Property(nameof(Bingo.FechaSorteoUtc)).CurrentValue = ahoraUtc.AddDays(-1);
        await _context.SaveChangesAsync();

        var resultadoActivo = await _repository.ObtenerParaCarritoAsync(cartonActivo.Id, ahoraUtc);
        var resultadoInexistente = await _repository.ObtenerParaCarritoAsync(Guid.NewGuid(), ahoraUtc);
        var resultadoVencido = await _repository.ObtenerParaCarritoAsync(cartonVencido.Id, ahoraUtc);

        Assert.NotNull(resultadoActivo);
        Assert.Equal(cartonActivo.Id, resultadoActivo!.CartonId);
        Assert.Equal(bingoActivo.Id, resultadoActivo.BingoId);
        Assert.Equal(150m, resultadoActivo.PrecioUnitario);
        Assert.Equal("Club Activo Carrito", resultadoActivo.NombreOrganizacion);
        Assert.Equal(bingoActivo.NombreEvento, resultadoActivo.NombreEvento);

        Assert.Null(resultadoInexistente);
        Assert.Null(resultadoVencido);
    }

    // Spec FEAT-009c, Block 3 (FR-05): ambos métodos excluyen CompraCartones cuya Compra está
    // Cancelada (JOIN a Compras, filtro Estado != Cancelado) — un bingo cuya única compra fue
    // cancelada vuelve a ser "sin compras", y su cartón vuelve a estar disponible para el carrito.
    [Fact]
    public async Task TieneComprasRegistradasAsync_ConLaUnicaCompraCancelada_DevuelveFalse()
    {
        var organizadorId = Guid.NewGuid();
        var ahoraUtc = DateTime.UtcNow;
        var bingo = NuevoBingo(organizadorId, ahoraUtc.AddDays(5), ahoraUtc);
        var carton = NuevoCarton(bingo.Id, numeroCorrelativo: 1, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10);
        _context.Bingos.Add(bingo);
        _context.Cartones.Add(carton);
        await _context.SaveChangesAsync();

        var compra = SembrarCompraCancelada(organizadorId, Guid.NewGuid());
        _context.Compras.Add(compra);
        _context.CompraCartones.Add(new CompraCarton { CompraId = compra.Id, CartonId = carton.Id, PrecioUnitario = 100m });
        await _context.SaveChangesAsync();

        var resultado = await _repository.TieneComprasRegistradasAsync(bingo.Id);

        Assert.False(resultado);
    }

    [Fact]
    public async Task ObtenerParaCarritoAsync_ConCartonDeCompraCancelada_LoDevuelveComoDisponible()
    {
        var ahoraUtc = DateTime.UtcNow;
        var organizador = NuevoOrganizador(Guid.NewGuid(), "Club Cancelada Carrito");
        var bingo = NuevoBingo(organizador.Id, ahoraUtc.AddDays(5), ahoraUtc, costoPorCarton: 130m);
        var carton = NuevoCarton(bingo.Id, numeroCorrelativo: 1, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10);

        _context.Users.Add(organizador);
        _context.Bingos.Add(bingo);
        _context.Cartones.Add(carton);
        await _context.SaveChangesAsync();

        var compra = SembrarCompraCancelada(organizador.Id, Guid.NewGuid());
        _context.Compras.Add(compra);
        _context.CompraCartones.Add(new CompraCarton { CompraId = compra.Id, CartonId = carton.Id, PrecioUnitario = 130m });
        await _context.SaveChangesAsync();

        var resultado = await _repository.ObtenerParaCarritoAsync(carton.Id, ahoraUtc);

        Assert.NotNull(resultado);
        Assert.Equal(carton.Id, resultado!.CartonId);
    }

    // Spec FEAT-009d, Block 2 (FR-12)
    [Fact]
    public async Task ObtenerParaCarritoAsync_IncluyeElNumeroCorrelativoDelCarton()
    {
        var ahoraUtc = DateTime.UtcNow;
        var organizador = NuevoOrganizador(Guid.NewGuid(), "Club Correlativo Carrito");
        var bingo = NuevoBingo(organizador.Id, ahoraUtc.AddDays(5), ahoraUtc, costoPorCarton: 130m);
        var carton = NuevoCarton(bingo.Id, numeroCorrelativo: 7, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10);

        _context.Users.Add(organizador);
        _context.Bingos.Add(bingo);
        _context.Cartones.Add(carton);
        await _context.SaveChangesAsync();

        var resultado = await _repository.ObtenerParaCarritoAsync(carton.Id, ahoraUtc);

        Assert.NotNull(resultado);
        Assert.Equal(carton.Id, resultado!.CartonId);
        Assert.Equal(7, resultado.NumeroCorrelativo);
    }

    // Spec FEAT-009d, Block 2 (FR-12): CartonParaConfirmarCompra lleva el correlativo hasta la
    // confirmación de compra, de donde lo toma el mail (EnvioMailRepository) y el PDF.
    [Fact]
    public async Task ObtenerParaConfirmarCompraAsync_IncluyeElNumeroCorrelativoDeCadaCarton()
    {
        var ahoraUtc = DateTime.UtcNow;
        var organizador = NuevoOrganizador(Guid.NewGuid(), "Club Correlativo Confirmar");
        var bingo = NuevoBingo(organizador.Id, ahoraUtc.AddDays(5), ahoraUtc);
        var cartonUno = NuevoCarton(bingo.Id, numeroCorrelativo: 3, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10);
        var cartonDos = NuevoCarton(bingo.Id, numeroCorrelativo: 9, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20);

        _context.Users.Add(organizador);
        _context.Bingos.Add(bingo);
        _context.Cartones.AddRange(cartonUno, cartonDos);
        await _context.SaveChangesAsync();

        var resultado = await _repository.ObtenerParaConfirmarCompraAsync(new[] { cartonUno.Id, cartonDos.Id });

        Assert.Equal(2, resultado.Count);
        Assert.Equal(3, Assert.Single(resultado, r => r.CartonId == cartonUno.Id).NumeroCorrelativo);
        Assert.Equal(9, Assert.Single(resultado, r => r.CartonId == cartonDos.Id).NumeroCorrelativo);
    }
}
