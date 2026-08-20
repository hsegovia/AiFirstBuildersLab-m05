using BingoCart.Application.Carritos.Dtos;
using BingoCart.Application.Descubrimiento.Dtos;

namespace BingoCart.Application.Carritos;

/// <summary>
/// Orquesta el carrito de compras (spec FEAT-008b, Block 2) — el único puerto que combina Redis
/// (<see cref="ICarritoRepository"/>) y SQL Server (<see cref="Bingos.IBingoRepository"/>,
/// <see cref="Descubrimiento.IDescubrimientoRepository"/>). Application solo conoce esta firma —
/// fijar/leer la cookie de sesión es responsabilidad del Controller (Block 3).
/// </summary>
public interface ICarritoService
{
    /// <summary>
    /// Agrega <paramref name="cartonId"/> al carrito de <paramref name="sesionId"/> con el precio
    /// vigente de su bingo (snapshot). Lanza <see cref="Domain.Carritos.Exceptions.CartonInexistenteException"/>
    /// si el cartón no existe o su bingo ya no está activo, o
    /// <see cref="Domain.Carritos.Exceptions.CartonYaReservadoException"/> si ya está reservado por
    /// otra sesión (FR-10/AC-09).
    /// </summary>
    Task AgregarAsync(string sesionId, Guid cartonId);

    /// <summary>
    /// Quita <paramref name="cartonId"/> del carrito de <paramref name="sesionId"/>. Idempotente:
    /// nunca lanza, sin importar si el cartón estaba o no en el carrito.
    /// </summary>
    Task QuitarAsync(string sesionId, Guid cartonId);

    /// <summary>
    /// Devuelve el carrito acumulado de <paramref name="sesionId"/> con cantidad y monto total
    /// (FR-09). Sesión sin carrito o expirado por TTL (FR-08) → carrito vacío, sin distinguir entre
    /// esos casos.
    /// </summary>
    Task<CarritoResponse> ObtenerCarritoAsync(string sesionId);

    /// <summary>
    /// Descarta <paramref name="descartadosDeEstaTanda"/> y devuelve una nueva tanda de hasta 5
    /// cartones al azar entre todos los bingos activos (Método 1 de descubrimiento, FEAT-008a),
    /// excluyendo tanto lo ya agregado al carrito de <paramref name="sesionId"/> como todo lo
    /// descartado históricamente por esa misma sesión.
    /// </summary>
    Task<IReadOnlyList<CartonDescubiertoResponse>> PedirNuevaTandaGlobalAsync(
        string sesionId, IReadOnlyCollection<Guid> descartadosDeEstaTanda);

    /// <summary>
    /// Igual que <see cref="PedirNuevaTandaGlobalAsync"/> pero acotado al bingo activo de
    /// <paramref name="organizadorId"/> (Método 2 de descubrimiento, FEAT-008a).
    /// </summary>
    Task<IReadOnlyList<CartonDescubiertoResponse>> PedirNuevaTandaPorOrganizadorAsync(
        string sesionId, Guid organizadorId, IReadOnlyCollection<Guid> descartadosDeEstaTanda);
}
