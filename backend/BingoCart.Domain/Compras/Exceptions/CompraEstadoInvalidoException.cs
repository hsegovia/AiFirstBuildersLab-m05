using BingoCart.Domain.Common;

namespace BingoCart.Domain.Compras.Exceptions;

/// <summary>
/// Se lanza cuando se intenta transicionar una <see cref="Compra"/> (<c>ConfirmarPago</c>/
/// <c>Cancelar</c>) desde un estado distinto de <see cref="EstadoCompra.PendienteConfirmacionPago"/>
/// (FR-02/AC-02, FR-04/AC-04) — nunca un no-op silencioso.
/// </summary>
public sealed class CompraEstadoInvalidoException : DomainException
{
    public CompraEstadoInvalidoException(string message)
        : base(message)
    {
    }
}
