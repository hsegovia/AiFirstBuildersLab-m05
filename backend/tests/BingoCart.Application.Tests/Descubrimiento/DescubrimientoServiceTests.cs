using BingoCart.Application.Descubrimiento;
using BingoCart.Application.Descubrimiento.Dtos;
using BingoCart.Domain.Bingos;
using BingoCart.Domain.Organizadores.Exceptions;
using Moq;

namespace BingoCart.Application.Tests.Descubrimiento;

/// <summary>
/// Tests unitarios de <see cref="DescubrimientoService"/> (spec FEAT-008a, Block 2) — mock de
/// <see cref="IDescubrimientoRepository"/>, mismo patrón que <c>BingoServiceTests</c>/
/// <c>OrganizadorServiceTests</c>.
/// </summary>
public class DescubrimientoServiceTests
{
    private static readonly Guid OrganizadorId = Guid.NewGuid();

    private static DescubrimientoService CrearService(Mock<IDescubrimientoRepository> repository) =>
        new(repository.Object, TimeProvider.System);

    private static Carton NuevoCarton(Guid bingoId, int inicio = 1) =>
        Carton.Crear(bingoId, Enumerable.Range(inicio, 10).ToList());

    [Fact]
    public async Task DescubrirGlobalAsync_ConCartonesDeDosBingosDistintos_ArmaCadaResponseConElResumenQueLeCorresponde()
    {
        var bingoUnoId = Guid.NewGuid();
        var bingoDosId = Guid.NewGuid();
        var cartonUno = NuevoCarton(bingoUnoId, inicio: 1);
        var cartonDos = NuevoCarton(bingoDosId, inicio: 11);

        var resumenUno = new BingoResumen(bingoUnoId, "Club Uno", "Bingo Uno", 100m, DateTime.UtcNow.AddDays(5));
        var resumenDos = new BingoResumen(bingoDosId, "Club Dos", "Bingo Dos", 200m, DateTime.UtcNow.AddDays(6));

        var repository = new Mock<IDescubrimientoRepository>();
        repository
            .Setup(r => r.ObtenerAleatoriosGlobalAsync(It.IsAny<DateTime>(), 5, It.IsAny<IReadOnlyCollection<Guid>>()))
            .ReturnsAsync(new List<Carton> { cartonUno, cartonDos });
        repository
            .Setup(r => r.ObtenerResumenBingosAsync(It.Is<IReadOnlyCollection<Guid>>(
                ids => ids.Count == 2 && ids.Contains(bingoUnoId) && ids.Contains(bingoDosId))))
            .ReturnsAsync(new List<BingoResumen> { resumenUno, resumenDos });

        var service = CrearService(repository);

        var response = await service.DescubrirGlobalAsync();

        Assert.Equal(2, response.Count);

        var respuestaUno = Assert.Single(response, r => r.Id == cartonUno.Id);
        Assert.Equal("Club Uno", respuestaUno.NombreOrganizacion);
        Assert.Equal("Bingo Uno", respuestaUno.NombreEvento);
        Assert.Equal(100m, respuestaUno.CostoPorCarton);
        Assert.Equal(resumenUno.FechaSorteoUtc, respuestaUno.FechaSorteoUtc);
        Assert.Equal(cartonUno.Numeros, respuestaUno.Numeros);

        var respuestaDos = Assert.Single(response, r => r.Id == cartonDos.Id);
        Assert.Equal("Club Dos", respuestaDos.NombreOrganizacion);
        Assert.Equal("Bingo Dos", respuestaDos.NombreEvento);
        Assert.Equal(200m, respuestaDos.CostoPorCarton);
        Assert.Equal(resumenDos.FechaSorteoUtc, respuestaDos.FechaSorteoUtc);
        Assert.Equal(cartonDos.Numeros, respuestaDos.Numeros);
    }

    [Fact]
    public async Task DescubrirGlobalAsync_ConRepositorioDevolviendoListaVacia_DevuelveListaVaciaSinInvocarObtenerResumenBingosAsync()
    {
        var repository = new Mock<IDescubrimientoRepository>();
        repository
            .Setup(r => r.ObtenerAleatoriosGlobalAsync(It.IsAny<DateTime>(), 5, It.IsAny<IReadOnlyCollection<Guid>>()))
            .ReturnsAsync(Array.Empty<Carton>());

        var service = CrearService(repository);

        var response = await service.DescubrirGlobalAsync();

        Assert.Empty(response);
        repository.Verify(
            r => r.ObtenerResumenBingosAsync(It.IsAny<IReadOnlyCollection<Guid>>()),
            Times.Never());
    }

    [Fact]
    public async Task DescubrirPorOrganizadorAsync_ConOrganizadorInexistente_LanzaExcepcionSinInvocarObtenerBingoActivoDeOrganizadorAsync()
    {
        var repository = new Mock<IDescubrimientoRepository>();
        repository.Setup(r => r.ExisteOrganizadorAsync(OrganizadorId)).ReturnsAsync(false);

        var service = CrearService(repository);

        await Assert.ThrowsAsync<OrganizadorNoEncontradoException>(() =>
            service.DescubrirPorOrganizadorAsync(OrganizadorId));

        repository.Verify(
            r => r.ObtenerBingoActivoDeOrganizadorAsync(It.IsAny<Guid>(), It.IsAny<DateTime>()),
            Times.Never());
    }

    [Fact]
    public async Task DescubrirPorOrganizadorAsync_ConOrganizadorExistenteYSinBingoActivo_DevuelveListaVaciaSinInvocarObtenerAleatoriosDeBingoAsync()
    {
        var repository = new Mock<IDescubrimientoRepository>();
        repository.Setup(r => r.ExisteOrganizadorAsync(OrganizadorId)).ReturnsAsync(true);
        repository
            .Setup(r => r.ObtenerBingoActivoDeOrganizadorAsync(OrganizadorId, It.IsAny<DateTime>()))
            .ReturnsAsync((Guid?)null);

        var service = CrearService(repository);

        var response = await service.DescubrirPorOrganizadorAsync(OrganizadorId);

        Assert.Empty(response);
        repository.Verify(
            r => r.ObtenerAleatoriosDeBingoAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<IReadOnlyCollection<Guid>>()),
            Times.Never());
    }

    [Fact]
    public async Task DescubrirPorOrganizadorAsync_ConOrganizadorConBingoActivo_ArmaLaRespuestaConLosDatosDeEseUnicoBingo()
    {
        var bingoId = Guid.NewGuid();
        var carton = NuevoCarton(bingoId);
        var resumen = new BingoResumen(bingoId, "Club Activo", "Bingo Activo", 150m, DateTime.UtcNow.AddDays(3));

        var repository = new Mock<IDescubrimientoRepository>();
        repository.Setup(r => r.ExisteOrganizadorAsync(OrganizadorId)).ReturnsAsync(true);
        repository
            .Setup(r => r.ObtenerBingoActivoDeOrganizadorAsync(OrganizadorId, It.IsAny<DateTime>()))
            .ReturnsAsync((Guid?)bingoId);
        repository
            .Setup(r => r.ObtenerAleatoriosDeBingoAsync(bingoId, 5, It.IsAny<IReadOnlyCollection<Guid>>()))
            .ReturnsAsync(new List<Carton> { carton });
        repository
            .Setup(r => r.ObtenerResumenBingosAsync(It.Is<IReadOnlyCollection<Guid>>(
                ids => ids.Count == 1 && ids.Contains(bingoId))))
            .ReturnsAsync(new List<BingoResumen> { resumen });

        var service = CrearService(repository);

        var response = await service.DescubrirPorOrganizadorAsync(OrganizadorId);

        var respuesta = Assert.Single(response);
        Assert.Equal(carton.Id, respuesta.Id);
        Assert.Equal("Club Activo", respuesta.NombreOrganizacion);
        Assert.Equal("Bingo Activo", respuesta.NombreEvento);
        Assert.Equal(150m, respuesta.CostoPorCarton);
        Assert.Equal(resumen.FechaSorteoUtc, respuesta.FechaSorteoUtc);
        Assert.Equal(carton.Numeros, respuesta.Numeros);
    }
}
