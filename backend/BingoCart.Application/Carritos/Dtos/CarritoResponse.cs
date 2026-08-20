namespace BingoCart.Application.Carritos.Dtos;

/// <summary>
/// Respuesta de <c>GET /api/carrito</c> (spec FEAT-008b, Block 2/3): los ítems ya enriquecidos junto
/// con la cantidad y el monto total, calculados por <see cref="Domain.Carritos.Carrito"/> (Domain,
/// Block 1) sobre los ítems que sí pudieron resolverse contra SQL Server.
/// </summary>
public sealed record CarritoResponse(
    IReadOnlyList<ItemCarritoResponse> Items,
    int CantidadTotal,
    decimal MontoTotal);
