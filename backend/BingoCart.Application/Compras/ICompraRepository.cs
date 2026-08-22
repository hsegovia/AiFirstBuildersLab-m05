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
}
