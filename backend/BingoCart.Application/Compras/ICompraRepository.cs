using BingoCart.Domain.Bingos;
using BingoCart.Domain.Compras;

namespace BingoCart.Application.Compras;

/// <summary>
/// Puerto de persistencia de <see cref="Compra"/> (spec FEAT-009a, Block 1). Infraestructura pura —
/// no decide negocio (qué compras se generan por organizador lo decide Application, Block 2, vía
/// <c>GroupBy(OrganizadorId)</c>).
/// </summary>
public interface ICompraRepository
{
    /// <summary>
    /// Persiste <paramref name="compras"/> en una única transacción EF Core ("todo o nada" real): si
    /// alguna viola el índice <c>UNIQUE</c> de <c>CompraCartones.CartonId</c> (carrera perdida contra
    /// otra confirmación), NINGUNA de las compras del intento queda persistida. Lanza
    /// <c>ReservaCarritoInvalidaException</c> (Domain) directamente en ese caso — la traducción desde
    /// el error de EF Core ocurre dentro de la implementación de Infrastructure, no acá: Application
    /// no depende de EF Core (corrective round 2, spec FEAT-009a Block 2).
    /// </summary>
    Task CrearVariasAsync(IReadOnlyList<Compra> compras);

    /// <summary>
    /// Devuelve la <see cref="Compra"/> con <paramref name="id"/>, trackeada por el
    /// <c>DbContext</c> (spec FEAT-009c, Block 2) — mismo patrón que
    /// <c>IBingoRepository.ObtenerPorIdAsync</c>: así una mutación posterior vía
    /// <c>Compra.ConfirmarPago</c>/<c>Cancelar</c> queda detectada por EF Core sin un
    /// <c>Update()</c> explícito. <c>null</c> si no existe.
    /// </summary>
    Task<Compra?> ObtenerPorIdAsync(Guid id);

    /// <summary>
    /// Persiste los cambios pendientes en el <c>DbContext</c> (spec FEAT-009c, Block 2). Necesario
    /// porque <c>Compra.ConfirmarPago</c>/<c>Cancelar</c> mutan la instancia ya trackeada devuelta
    /// por <see cref="ObtenerPorIdAsync"/> — mismo verbo que <c>IBingoRepository</c>.
    /// </summary>
    Task GuardarCambiosAsync();

    /// <summary>
    /// Devuelve la página <paramref name="page"/> (1-based) de tamaño <paramref name="pageSize"/> de
    /// las compras de <paramref name="organizadorId"/>, junto con el total real (sin paginar) —
    /// spec FEAT-009c, Block 2. <paramref name="page"/>/<paramref name="pageSize"/> se asumen ya
    /// validados/clampeados por el llamador (Application, <c>CompraOrganizadorService</c>) — este
    /// puerto no revalida.
    /// </summary>
    Task<ComprasPaginadas> ListarPorOrganizadorAsync(Guid organizadorId, int page, int pageSize);

    /// <summary>
    /// Devuelve el monto total de <paramref name="compraId"/> (suma de
    /// <c>CompraCartones.PrecioUnitario</c> de esa compra) — spec FEAT-009c, Block 3 (corrección
    /// post-implementación: <c>ConfirmarPagoAsync</c>/<c>CancelarAsync</c> necesitan devolver un
    /// <c>CompraResumenResponse</c> completo sin depender de que la compra recién mutada caiga dentro
    /// de la primera página de <see cref="ListarPorOrganizadorAsync"/>). El monto de una compra no
    /// cambia al confirmar/cancelar (cancelar no borra <c>CompraCartones</c>), así que una sola lectura
    /// alcanza. Devuelve 0 si la compra no tiene ítems (no debería ocurrir en la práctica, pero evita
    /// una excepción sobre una colección vacía).
    /// </summary>
    Task<decimal> ObtenerMontoTotalAsync(Guid compraId);

    /// <summary>
    /// Devuelve la página <paramref name="page"/> (1-based) de tamaño <paramref name="pageSize"/> de
    /// los cartones adquiridos por <paramref name="compradorId"/> — spec FEAT-009d, Block 3 (FR-11).
    /// Join de cuatro tablas (<c>CompraCartones</c> → <c>Compras</c> → <c>Cartones</c> → <c>Bingos</c>
    /// → <c>AspNetUsers</c>), filtrado por <c>Compras.CompradorId</c>. Incluye cartones de compras en
    /// CUALQUIER estado, incluidas las canceladas (FR-02/AC-02/A-01) — a diferencia de las consultas
    /// de disponibilidad de venta (FEAT-009c), acá el criterio es el opuesto: el comprador necesita
    /// ver qué pasó con su compra. Orden: <c>Compra.FechaCreacionUtc</c> descendente, desempatado por
    /// <c>Carton.NumeroCorrelativo</c> ascendente (esa columna no es única — dos cartones de la misma
    /// compra comparten fecha — así que sin desempate la paginación no sería estable entre páginas).
    /// <paramref name="page"/>/<paramref name="pageSize"/> se asumen ya validados/clampeados por el
    /// llamador (Application, <c>MisCartonesService</c>) — este puerto no revalida.
    /// </summary>
    Task<CartonesAdquiridosPaginados> ListarCartonesDelCompradorAsync(Guid compradorId, int page, int pageSize);

    /// <summary>
    /// Devuelve el <see cref="Carton"/> con <paramref name="cartonId"/> SOLO si pertenece a alguna
    /// compra de <paramref name="compradorId"/> — spec FEAT-009d, Block 4 (FR-05/AC-05). Cruza
    /// <c>CompraCartones</c> con <c>Compras</c> filtrando por <c>CompradorId</c>, mismo criterio de
    /// pertenencia que <see cref="ListarCartonesDelCompradorAsync"/>. <c>null</c> tanto si el cartón
    /// no existe como si pertenece a otro comprador — Application (<c>MisCartonesService</c>) traduce
    /// ambos casos a la misma <c>CartonNoEncontradoException</c>, sin distinguirlos
    /// (anti-enumeración, R-01, precedente <c>ObtenerCompraPropiaAsync</c> de FEAT-009c).
    /// </summary>
    Task<Carton?> ObtenerCartonDelCompradorAsync(Guid compradorId, Guid cartonId);
}
