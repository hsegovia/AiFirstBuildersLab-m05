using BingoCart.Application.Carritos.Dtos;
using BingoCart.Domain.Carritos;

namespace BingoCart.Application.Carritos;

/// <summary>
/// Puerto de infraestructura pura para el estado del carrito, todo en Redis (spec FEAT-008b, Block
/// 1) — nunca lanza excepciones de dominio: <see cref="IntentarAgregarAsync"/> señaliza el conflicto
/// con el valor de retorno, mismo criterio ya usado en <c>DescubrimientoRepository</c>. Application
/// (Block 2) traduce ese <c>false</c> a <see cref="Domain.Carritos.Exceptions.CartonYaReservadoException"/>.
/// No valida que <c>cartonId</c> corresponda a un cartón real ni que su bingo siga activo — eso lo
/// hace Application contra SQL antes de llamar acá.
/// </summary>
public interface ICarritoRepository
{
    /// <summary>
    /// Intenta reservar <paramref name="cartonId"/> para <paramref name="sesionId"/> y agregarlo al
    /// carrito con <paramref name="precioUnitario"/> (snapshot). Atómico vía script Lua (un único
    /// round-trip): si el cartón ya está reservado por OTRA sesión devuelve <c>false</c> sin
    /// modificar nada; si está libre o ya pertenece a esta misma sesión, lo reserva/actualiza y
    /// refresca a <paramref name="ttl"/> el TTL de TODAS las reservas ya presentes en el carrito
    /// (FR-06/FR-07 — un agregado reinicia el plazo de todo el carrito, no solo del ítem nuevo).
    /// </summary>
    Task<bool> IntentarAgregarAsync(string sesionId, Guid cartonId, decimal precioUnitario, TimeSpan ttl);

    /// <summary>
    /// Quita <paramref name="cartonId"/> del carrito de <paramref name="sesionId"/> y libera su
    /// reserva. Idempotente: no falla si el cartón no estaba en el carrito. Nunca modifica el TTL de
    /// ninguna otra clave (NFR-03) — a diferencia de <see cref="IntentarAgregarAsync"/>, no refresca
    /// nada.
    /// </summary>
    Task QuitarAsync(string sesionId, Guid cartonId);

    /// <summary>
    /// Devuelve los ítems actualmente en el carrito de <paramref name="sesionId"/>. Carrito vacío,
    /// nunca usado, o expirado por TTL (FR-08) → lista vacía, sin distinguir entre esos casos.
    /// </summary>
    Task<IReadOnlyList<ItemCarrito>> ObtenerItemsAsync(string sesionId);

    /// <summary>
    /// Agrega <paramref name="cartonIds"/> al historial de descartados de <paramref name="sesionId"/>
    /// y refresca el TTL de esa clave a <paramref name="ttl"/> — a diferencia del carrito, la clave
    /// de descartados SÍ refresca su TTL en cada llamada (decisión de PLAN).
    /// </summary>
    Task AgregarDescartadosAsync(string sesionId, IReadOnlyCollection<Guid> cartonIds, TimeSpan ttl);

    /// <summary>
    /// Devuelve el historial de cartones descartados de <paramref name="sesionId"/>. Sin descartes
    /// previos o expirado por TTL → conjunto vacío.
    /// </summary>
    Task<IReadOnlySet<Guid>> ObtenerDescartadosAsync(string sesionId);

    /// <summary>
    /// Confirma atómicamente, de SOLO LECTURA (spec FEAT-009a, Block 1 — decisión de PLAN "CHECK y
    /// COMMIT son dos operaciones separadas"), que cada <c>cartonId</c> del carrito de
    /// <paramref name="sesionId"/> sigue teniendo <c>reservado:carton:{cartonId} == sesionId</c>.
    /// Nunca borra ninguna clave, en ningún caso. Si todos son válidos devuelve
    /// <c>EsValido: true</c> con los ítems; si alguno no lo es, devuelve <c>EsValido: false</c> con
    /// la lista completa de <c>cartonId</c> inválidos.
    /// </summary>
    Task<RevalidacionCarrito> RevalidarReservasAsync(string sesionId);

    /// <summary>
    /// Borra <c>carrito:{sesionId}</c> y cada <c>reservado:carton:{cartonId}</c> de
    /// <paramref name="cartonIds"/> — SOLO se invoca después de que la transacción SQL de la compra
    /// commiteó exitosamente (decisión de PLAN, ver spec "Revalidación atómica de Redis").
    /// </summary>
    Task LiberarCarritoConfirmadoAsync(string sesionId, IReadOnlyCollection<Guid> cartonIds);
}
