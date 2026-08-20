using BingoCart.Domain.Compras;

namespace BingoCart.Application.Compras.Dtos;

/// <summary>
/// Request de confirmación de compra (spec FEAT-009a, Block 2) — solo el medio de pago: los datos
/// del comprador ya se pidieron una vez al registrarse (decisión de PLAN), y el carrito se resuelve
/// enteramente a partir de la cookie <c>bingocart_carrito</c> (Block 3).
/// </summary>
public sealed record ConfirmarCompraRequest(MedioPago MedioPago);
