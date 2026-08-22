using BingoCart.Domain.Compras;
using BingoCart.Domain.Compras.Exceptions;

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
            confirmacionId: Guid.NewGuid(),
            items: Array.Empty<ItemCompra>(),
            medioPago: MedioPago.Efectivo,
            ahoraUtc: DateTime.UtcNow));
    }

    [Fact]
    public void Crear_ConAlMenosUnItem_ConstruyeLaEntidadCorrectamente()
    {
        var organizadorId = Guid.NewGuid();
        var compradorId = Guid.NewGuid();
        var confirmacionId = Guid.NewGuid();
        var ahoraUtc = DateTime.UtcNow;

        var compra = Compra.Crear(organizadorId, compradorId, confirmacionId, UnItem, MedioPago.Transferencia, ahoraUtc);

        Assert.NotEqual(Guid.Empty, compra.Id);
        Assert.Equal(organizadorId, compra.OrganizadorId);
        Assert.Equal(compradorId, compra.CompradorId);
        Assert.Equal(UnItem, compra.Items);
        Assert.Equal(MedioPago.Transferencia, compra.MedioPago);
        Assert.Equal(EstadoCompra.PendienteConfirmacionPago, compra.Estado);
        Assert.Equal(ahoraUtc, compra.FechaCreacionUtc);
    }

    [Fact]
    public void Crear_ExponeConfirmacionId()
    {
        var confirmacionId = Guid.NewGuid();

        var compra = Compra.Crear(
            organizadorId: Guid.NewGuid(),
            compradorId: Guid.NewGuid(),
            confirmacionId: confirmacionId,
            items: UnItem,
            medioPago: MedioPago.Efectivo,
            ahoraUtc: DateTime.UtcNow);

        Assert.Equal(confirmacionId, compra.ConfirmacionId);
    }

    private static Compra CrearCompraPendiente()
    {
        return Compra.Crear(
            organizadorId: Guid.NewGuid(),
            compradorId: Guid.NewGuid(),
            confirmacionId: Guid.NewGuid(),
            items: UnItem,
            medioPago: MedioPago.Efectivo,
            ahoraUtc: DateTime.UtcNow);
    }

    [Fact]
    public void ConfirmarPago_DesdePendiente_TransicionaAConfirmado()
    {
        var compra = CrearCompraPendiente();

        compra.ConfirmarPago();

        Assert.Equal(EstadoCompra.Confirmado, compra.Estado);
    }

    [Fact]
    public void ConfirmarPago_DesdeConfirmado_LanzaCompraEstadoInvalidoException()
    {
        var compra = CrearCompraPendiente();
        compra.ConfirmarPago();

        var ex = Assert.Throws<CompraEstadoInvalidoException>(() => compra.ConfirmarPago());
        Assert.Equal("La compra no está pendiente de confirmación de pago.", ex.Message);
    }

    [Fact]
    public void ConfirmarPago_DesdeCancelado_LanzaCompraEstadoInvalidoException()
    {
        var compra = CrearCompraPendiente();
        compra.Cancelar();

        var ex = Assert.Throws<CompraEstadoInvalidoException>(() => compra.ConfirmarPago());
        Assert.Equal("La compra no está pendiente de confirmación de pago.", ex.Message);
    }

    [Fact]
    public void Cancelar_DesdePendiente_TransicionaACancelado()
    {
        var compra = CrearCompraPendiente();

        compra.Cancelar();

        Assert.Equal(EstadoCompra.Cancelado, compra.Estado);
    }

    [Fact]
    public void Cancelar_DesdeConfirmado_LanzaCompraEstadoInvalidoException()
    {
        var compra = CrearCompraPendiente();
        compra.ConfirmarPago();

        var ex = Assert.Throws<CompraEstadoInvalidoException>(() => compra.Cancelar());
        Assert.Equal("La compra no está pendiente de confirmación de pago.", ex.Message);
    }

    [Fact]
    public void Cancelar_DesdeCancelado_LanzaCompraEstadoInvalidoException()
    {
        var compra = CrearCompraPendiente();
        compra.Cancelar();

        var ex = Assert.Throws<CompraEstadoInvalidoException>(() => compra.Cancelar());
        Assert.Equal("La compra no está pendiente de confirmación de pago.", ex.Message);
    }
}
