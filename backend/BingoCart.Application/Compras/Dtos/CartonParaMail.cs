namespace BingoCart.Application.Compras.Dtos;

/// <summary>
/// Cartón comprado a incluir en el mail de confirmación (spec FEAT-009b, Block 2) — sus
/// <see cref="Numeros"/> se usan tanto en el cuerpo del mail (AC-02) como en el PDF adjunto
/// generado por <c>ICartonPdfRenderer</c> (AC-03). Desde FEAT-009d (Block 2, FR-12) lleva también
/// <see cref="NumeroCorrelativo"/>, que el PDF imprime (AC-04) para que el comprador vea el mismo
/// número que ya vio en el descubrimiento y en el carrito.
/// </summary>
public sealed record CartonParaMail(Guid CartonId, int NumeroCorrelativo, IReadOnlyList<int> Numeros);
