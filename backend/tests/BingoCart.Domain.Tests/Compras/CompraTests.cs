using BingoCart.Domain.Compras;

namespace BingoCart.Domain.Tests.Compras;

/// <summary>
/// Tests de <see cref="Compra"/> — agregado puro, sin I/O (spec FEAT-009a, Block 1). Valida la
/// invariante interna "items.Count > 0" (defensa en profundidad: la validación real de "carrito no
/// vacío" ya ocurre antes, en Application, Block 2).
/// </summary>
public class CompraTests
{
    private static readonly IReadOnlyList<ItemCompra> UnItem = new List<ItemCompra>
    {
        new(Guid.NewGuid(), 150m),
    };

    [Fact]
    public void Crear_ConItemsVacio_LanzaArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Compra.Crear(
            organizadorId: Guid.NewGuid(),
            compradorId: Guid.NewGuid(),
            items: Array.Empty<ItemCompra>(),
            medioPago: MedioPago.Efectivo,
            ahoraUtc: DateTime.UtcNow));
    }

    [Fact]
    public void Crear_ConAlMenosUnItem_ConstruyeLaEntidadCorrectamente()
    {
        var organizadorId = Guid.NewGuid();
        var compradorId = Guid.NewGuid();
        var ahoraUtc = DateTime.UtcNow;

        var compra = Compra.Crear(organizadorId, compradorId, UnItem, MedioPago.Transferencia, ahoraUtc);

        Assert.NotEqual(Guid.Empty, compra.Id);
        Assert.Equal(organizadorId, compra.OrganizadorId);
        Assert.Equal(compradorId, compra.CompradorId);
        Assert.Equal(UnItem, compra.Items);
        Assert.Equal(MedioPago.Transferencia, compra.MedioPago);
        Assert.Equal(EstadoCompra.PendienteConfirmacionPago, compra.Estado);
        Assert.Equal(ahoraUtc, compra.FechaCreacionUtc);
    }
}
