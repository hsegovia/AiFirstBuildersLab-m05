using BingoCart.Domain.Common;

namespace BingoCart.Domain.Compradores.Exceptions;

/// <summary>
/// Se lanza cuando el comprador intenta actualizar sus datos de cuenta y alguno de sus sorteos
/// ocurre dentro de la ventana <c>[ahora, ahora + 60 min]</c> (FR-07, NFR-05, D-01, D-02). Los
/// sorteos ya pasados NO bloquean —esa lectura literal congelaría la cuenta para siempre tras la
/// primera compra— y las compras <c>Cancelado</c> quedan excluidas del cálculo: ya no tienen
/// cartones vigentes que puedan generar la disputa que esta regla previene.
/// </summary>
public sealed class PlazoModificacionVencidoException : DomainException
{
    public PlazoModificacionVencidoException(string message)
        : base(message)
    {
    }
}
