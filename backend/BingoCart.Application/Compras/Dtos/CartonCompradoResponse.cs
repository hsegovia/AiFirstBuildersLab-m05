namespace BingoCart.Application.Compras.Dtos;

/// <summary>
/// Un cartón de una <see cref="CompraCreada"/>, tal como lo devuelve la respuesta de confirmar la
/// compra (spec FEAT-009d, Block 2 — FR-12/AC-14). Lleva solo el identificador y el correlativo, y
/// eso es deliberado: la confirmación no es un listado, es el acuse de la operación, y el número
/// correlativo es lo único que el comprador necesita para reconocer ahí mismo el cartón que ya vio
/// en el descubrimiento y en el carrito. El detalle completo —números, bingo, estado de pago— es
/// asunto del listado de cartones adquiridos, que tiene su propio DTO.
/// </summary>
public sealed record CartonCompradoResponse(Guid CartonId, int NumeroCorrelativo);
