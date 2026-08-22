using System;
using System.Linq;
using System.Threading.Tasks;
using BingoCart.Application.Compras;
using BingoCart.Domain.Bingos;
using BingoCart.Domain.Compras;
using BingoCart.Infrastructure.Compras;
using BingoCart.Infrastructure.Data;
using BingoCart.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace BingoCart.Infrastructure.Tests.Compras;

/// <summary>
/// Tests de integración de <see cref="EnvioMailRepository"/> contra SQL Server real (spec
/// FEAT-009b, Block 3) — mismo patrón que <c>CompraRepositoryTests</c>/<c>DescubrimientoRepositoryTests</c>:
/// base propia y descartable (<c>BingoCartTests_EnvioMailRepository</c>), migrada al inicio y
/// eliminada en <see cref="DisposeAsync"/> (Rule #0 de testing.instructions.md).
/// </summary>
public sealed class EnvioMailRepositoryTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Server=localhost,14330;Database=BingoCartTests_EnvioMailRepository;User Id=sa;" +
        "Password=BingoCart_Dev2026!;TrustServerCertificate=True;Encrypt=True;";

    private AppDbContext _context = null!;
    private IEnvioMailRepository _repository = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        _context = new AppDbContext(options);
        await _context.Database.MigrateAsync();

        _repository = new EnvioMailRepository(_context);
    }

    public async Task DisposeAsync()
    {
        await _context.Database.EnsureDeletedAsync();
        await _context.DisposeAsync();
    }

    private static ApplicationUser NuevoUsuario(Guid id, string? nombreOrganizacion = null, string? nombre = null, string? apellido = null) => new()
    {
        Id = id,
        UserName = $"{id}@example.com",
        Email = $"{id}@example.com",
        NombreOrganizacion = nombreOrganizacion,
        Nombre = nombre,
        Apellido = apellido,
        Cuit = id.ToString("N")[..11],
        Telefono = "+54 11 4444-5555",
    };

    [Fact]
    public async Task ObtenerPendientesAsync_RespetaElFiltroDeEstadoYProximoIntento()
    {
        var ahoraUtc = DateTime.UtcNow;

        var pendienteSinProximoIntento = EnvioMail.Crear(Guid.NewGuid(), Guid.NewGuid(), ahoraUtc);

        var pendienteConProximoVencido = EnvioMail.Crear(Guid.NewGuid(), Guid.NewGuid(), ahoraUtc);
        pendienteConProximoVencido.RegistrarIntentoFallido(ahoraUtc.AddMinutes(-5));

        var pendienteConProximoFuturo = EnvioMail.Crear(Guid.NewGuid(), Guid.NewGuid(), ahoraUtc);
        pendienteConProximoFuturo.RegistrarIntentoFallido(ahoraUtc);

        var exitoso = EnvioMail.Crear(Guid.NewGuid(), Guid.NewGuid(), ahoraUtc);
        exitoso.RegistrarExito();

        var fallido = EnvioMail.Crear(Guid.NewGuid(), Guid.NewGuid(), ahoraUtc);
        fallido.RegistrarIntentoFallido(ahoraUtc.AddMinutes(-10));
        fallido.RegistrarIntentoFallido(ahoraUtc.AddMinutes(-9));
        fallido.RegistrarIntentoFallido(ahoraUtc.AddMinutes(-8));

        await _repository.EncolarAsync(pendienteSinProximoIntento);
        await _repository.EncolarAsync(pendienteConProximoVencido);
        await _repository.EncolarAsync(pendienteConProximoFuturo);
        await _repository.EncolarAsync(exitoso);
        await _repository.EncolarAsync(fallido);

        // "Reinicio" simulado: nuevo DbContext/repositorio contra el MISMO estado ya persistido
        // (AC-07) — ver comentario de clase.
        var optionsReinicio = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;
        await using var contextoReinicio = new AppDbContext(optionsReinicio);
        var repositorioReinicio = new EnvioMailRepository(contextoReinicio);

        var pendientes = await repositorioReinicio.ObtenerPendientesAsync(ahoraUtc);
        var idsPendientes = pendientes.Select(e => e.Id).ToList();

        Assert.Contains(pendienteSinProximoIntento.Id, idsPendientes);
        Assert.Contains(pendienteConProximoVencido.Id, idsPendientes);
        Assert.DoesNotContain(pendienteConProximoFuturo.Id, idsPendientes);
        Assert.DoesNotContain(exitoso.Id, idsPendientes);
        Assert.DoesNotContain(fallido.Id, idsPendientes);
    }

    [Fact]
    public async Task ObtenerDatosParaEnviarAsync_ArmaCorrectamenteElJoinDeCompradorYCompras()
    {
        var ahoraUtc = DateTime.UtcNow;
        var confirmacionId = Guid.NewGuid();

        var comprador = NuevoUsuario(Guid.NewGuid(), nombre: "Ana", apellido: "Gómez");
        var organizadorUno = NuevoUsuario(Guid.NewGuid(), nombreOrganizacion: "Club Uno");
        var organizadorDos = NuevoUsuario(Guid.NewGuid(), nombreOrganizacion: "Club Dos");

        var bingoUno = Bingo.Crear("Bingo Uno", ahoraUtc.AddDays(5), 100, 50m, organizadorUno.Id, ahoraUtc);
        var bingoDos = Bingo.Crear("Bingo Dos", ahoraUtc.AddDays(6), 100, 80m, organizadorDos.Id, ahoraUtc);

        var cartonUno = Carton.Crear(bingoUno.Id, new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
        var cartonDos = Carton.Crear(bingoDos.Id, new[] { 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 });

        var compraUno = Compra.Crear(
            organizadorUno.Id, comprador.Id, confirmacionId,
            new[] { new ItemCompra(cartonUno.Id, 50m) }, MedioPago.Efectivo, ahoraUtc);
        var compraDos = Compra.Crear(
            organizadorDos.Id, comprador.Id, confirmacionId,
            new[] { new ItemCompra(cartonDos.Id, 80m) }, MedioPago.Transferencia, ahoraUtc);

        _context.Users.AddRange(comprador, organizadorUno, organizadorDos);
        _context.Bingos.AddRange(bingoUno, bingoDos);
        _context.Cartones.AddRange(cartonUno, cartonDos);
        _context.Compras.AddRange(compraUno, compraDos);
        _context.CompraCartones.AddRange(
            new CompraCarton { CompraId = compraUno.Id, CartonId = cartonUno.Id, PrecioUnitario = 50m },
            new CompraCarton { CompraId = compraDos.Id, CartonId = cartonDos.Id, PrecioUnitario = 80m });
        await _context.SaveChangesAsync();

        var datos = await _repository.ObtenerDatosParaEnviarAsync(confirmacionId);

        Assert.NotNull(datos);
        Assert.Equal(comprador.Email, datos!.MailComprador);
        Assert.Equal("Ana", datos.NombreComprador);
        Assert.Equal("Gómez", datos.ApellidoComprador);
        Assert.Equal(2, datos.Compras.Count);

        var detalleUno = Assert.Single(datos.Compras, c => c.CompraId == compraUno.Id);
        Assert.Equal("Club Uno", detalleUno.NombreOrganizacion);
        Assert.Equal(50m, detalleUno.MontoTotal);
        var cartonDetalleUno = Assert.Single(detalleUno.Cartones);
        Assert.Equal(cartonUno.Id, cartonDetalleUno.CartonId);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, cartonDetalleUno.Numeros);

        var detalleDos = Assert.Single(datos.Compras, c => c.CompraId == compraDos.Id);
        Assert.Equal("Club Dos", detalleDos.NombreOrganizacion);
        Assert.Equal(80m, detalleDos.MontoTotal);
    }

    [Fact]
    public async Task ObtenerDatosParaEnviarAsync_SinNingunaCompraConEseConfirmacionId_DevuelveNull()
    {
        var datos = await _repository.ObtenerDatosParaEnviarAsync(Guid.NewGuid());

        Assert.Null(datos);
    }

    [Fact]
    public async Task EncolarAsync_y_ActualizarAsync_Persisten()
    {
        var ahoraUtc = DateTime.UtcNow;
        var envio = EnvioMail.Crear(Guid.NewGuid(), Guid.NewGuid(), ahoraUtc);

        await _repository.EncolarAsync(envio);

        var persistidoTrasEncolar = await _context.EnviosMail.AsNoTracking().SingleAsync(e => e.Id == envio.Id);
        Assert.Equal(EstadoEnvioMail.Pendiente, persistidoTrasEncolar.Estado);
        Assert.Equal(0, persistidoTrasEncolar.Intentos);
        Assert.Null(persistidoTrasEncolar.ProximoIntentoUtc);

        envio.RegistrarIntentoFallido(ahoraUtc);
        await _repository.ActualizarAsync(envio);

        var persistidoTrasActualizar = await _context.EnviosMail.AsNoTracking().SingleAsync(e => e.Id == envio.Id);
        Assert.Equal(EstadoEnvioMail.Pendiente, persistidoTrasActualizar.Estado);
        Assert.Equal(1, persistidoTrasActualizar.Intentos);
        Assert.NotNull(persistidoTrasActualizar.ProximoIntentoUtc);
    }
}
