using BingoCart.Application.Compras.Dtos;
using BingoCart.Domain.Compras;

namespace BingoCart.Application.Compras;

public interface ICompraService
{
    /// <summary>
    /// Confirma la compra del carrito de <paramref name="sesionId"/> para el comprador
    /// <paramref name="compradorId"/> (spec FEAT-009a, Block 2). <paramref name="sesionId"/> y
    /// <paramref name="compradorId"/> se asumen ya autenticados/no vacíos — el Controller (Block 3)
    /// lo garantiza.
    /// </summary>
    Task<ConfirmarCompraResponse> ConfirmarCompraAsync(string sesionId, Guid compradorId, MedioPago medioPago);
}
