using BingoCart.Domain.Common;

namespace BingoCart.Domain.Compras.Exceptions;

/// <summary>
/// Se lanza cuando el cartón pedido para descarga de PDF no existe o no pertenece al comprador
/// autenticado (spec FEAT-009d, Block 4, FR-05/AC-05) — mismo tipo para ambos casos, mismo patrón
/// de no-enumeración que <see cref="CompraNoEncontradaException"/> (precedente
/// <c>ObtenerCompraPropiaAsync</c> de FEAT-009c). Vive en este bounded context (<c>Compras</c>), a
/// propósito NO se reutiliza <c>BingoCart.Domain.Carritos.Exceptions.CartonInexistenteException</c>:
/// esa excepción pertenece al flujo de reserva de carrito, un contexto distinto.
/// </summary>
public sealed class CartonNoEncontradoException : DomainException
{
    public CartonNoEncontradoException(string message)
        : base(message)
    {
    }
}
