using BingoCart.Domain.Carritos;

namespace BingoCart.Application.Carritos.Dtos;

/// <summary>
/// Resultado de <see cref="ICarritoRepository.RevalidarReservasAsync"/> (spec FEAT-009a, Block 1):
/// confirma, atómicamente y de SOLO LECTURA, que cada ítem del carrito sigue teniendo su reserva de
/// Redis vigente. <see cref="EsValido"/> es <c>true</c> solo si TODOS los ítems son válidos; si
/// alguno no lo es, <see cref="Items"/> queda vacío y <see cref="CartonIdsInvalidos"/> lista los que
/// perdieron su reserva — decisión de PLAN: "CHECK y COMMIT son dos operaciones separadas".
/// </summary>
public sealed record RevalidacionCarrito(
    bool EsValido,
    IReadOnlyList<ItemCarrito> Items,
    IReadOnlyList<Guid> CartonIdsInvalidos);
