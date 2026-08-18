using BingoCart.Domain.Bingos;
using BingoCart.Domain.Bingos.Exceptions;

namespace BingoCart.Domain.Tests.Bingos;

public class BingoTests
{
    private static readonly Guid OrganizadorId = Guid.NewGuid();
    private static readonly DateTime AhoraUtc = new(2026, 8, 17, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime FechaSorteoFutura = AhoraUtc.AddDays(7);

    [Fact]
    public void Crear_ConDatosValidos_CreaElAgregado()
    {
        var bingo = Bingo.Crear(
            "Bingo Solidario",
            FechaSorteoFutura,
            100,
            500m,
            OrganizadorId,
            AhoraUtc);

        Assert.NotEqual(Guid.Empty, bingo.Id);
        Assert.Equal(OrganizadorId, bingo.OrganizadorId);
        Assert.Equal("Bingo Solidario", bingo.NombreEvento);
        Assert.Equal(FechaSorteoFutura, bingo.FechaSorteoUtc);
        Assert.Equal(AhoraUtc, bingo.FechaCreacionUtc);
        Assert.Equal(100, bingo.CantidadCartones);
        Assert.Equal(500m, bingo.CostoPorCarton);
    }

    [Fact]
    public void Crear_ConFechaSorteoEnElPasado_LanzaFechaSorteoInvalidaException()
    {
        var fechaPasada = AhoraUtc.AddMinutes(-1);

        Assert.Throws<FechaSorteoInvalidaException>(() =>
            Bingo.Crear("Bingo Solidario", fechaPasada, 100, 500m, OrganizadorId, AhoraUtc));
    }

    [Fact]
    public void Crear_ConCantidadCartonesQueExcedeElLimite_LanzaCantidadCartonesExcedeLimiteException()
    {
        Assert.Throws<CantidadCartonesExcedeLimiteException>(() =>
            Bingo.Crear("Bingo Solidario", FechaSorteoFutura, 5001, 500m, OrganizadorId, AhoraUtc));
    }

    [Fact]
    public void Crear_ConCantidadCartonesCero_LanzaCantidadCartonesInvalidaException()
    {
        Assert.Throws<CantidadCartonesInvalidaException>(() =>
            Bingo.Crear("Bingo Solidario", FechaSorteoFutura, 0, 500m, OrganizadorId, AhoraUtc));
    }

    [Fact]
    public void Crear_ConCostoPorCartonCero_LanzaCostoPorCartonInvalidoException()
    {
        Assert.Throws<CostoPorCartonInvalidoException>(() =>
            Bingo.Crear("Bingo Solidario", FechaSorteoFutura, 100, 0m, OrganizadorId, AhoraUtc));
    }
}
