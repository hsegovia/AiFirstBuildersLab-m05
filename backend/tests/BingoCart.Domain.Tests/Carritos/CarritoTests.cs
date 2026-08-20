using BingoCart.Domain.Carritos;

namespace BingoCart.Domain.Tests.Carritos;

/// <summary>
/// Tests de <see cref="Carrito"/> — aritmética pura sobre una lista ya cargada, sin I/O (spec
/// FEAT-008b, Block 1). Valida FR-09 (parte de dominio: recalcular cantidad/monto).
/// </summary>
public class CarritoTests
{
    [Fact]
    public void DeItems_ConTresItemsDePrecios100200300_CantidadTotalYMontoTotalCorrectos()
    {
        var items = new List<ItemCarrito>
        {
            new(Guid.NewGuid(), 100m),
            new(Guid.NewGuid(), 200m),
            new(Guid.NewGuid(), 300m),
        };

        var carrito = Carrito.DeItems(items);

        Assert.Equal(3, carrito.CantidadTotal);
        Assert.Equal(600m, carrito.MontoTotal);
    }

    [Fact]
    public void DeItems_ConListaVacia_CantidadTotalYMontoTotalSonCero()
    {
        var carrito = Carrito.DeItems(Array.Empty<ItemCarrito>());

        Assert.Equal(0, carrito.CantidadTotal);
        Assert.Equal(0m, carrito.MontoTotal);
    }
}
