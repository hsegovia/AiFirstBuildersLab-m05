namespace BingoCart.Application.Compras.Dtos;

/// <summary>
/// Cartón comprado a incluir en el mail de confirmación (spec FEAT-009b, Block 2) — sus
/// <see cref="Numeros"/> se usan tanto en el cuerpo del mail (AC-02) como en el PDF adjunto
/// generado por <c>ICartonPdfRenderer</c> (AC-03).
/// </summary>
public sealed record CartonParaMail(Guid CartonId, IReadOnlyList<int> Numeros);
