namespace BingoCart.Application.Compras;

/// <summary>
/// Servicio de aplicación para el outbox de mail de confirmación/cancelación de compra (spec
/// FEAT-009b, Block 2; extendido en FEAT-009c, Block 2).
/// </summary>
public interface IEnvioMailService
{
    /// <summary>
    /// Encola un envío en estado Pendiente para la confirmación <paramref name="confirmacionId"/>
    /// del comprador <paramref name="compradorId"/> (FR-02). Renombrado desde <c>EncolarAsync</c>
    /// (FEAT-009c, Block 2) por simetría con <see cref="EncolarCancelacionAsync"/>.
    /// </summary>
    Task EncolarConfirmacionAsync(Guid confirmacionId, Guid compradorId);

    /// <summary>
    /// Encola un envío en estado Pendiente para la cancelación de la compra
    /// <paramref name="compraId"/> del comprador <paramref name="compradorId"/> (FR-06, FEAT-009c).
    /// </summary>
    Task EncolarCancelacionAsync(Guid compraId, Guid compradorId);

    /// <summary>
    /// Procesa todos los envíos pendientes listos para (re)intentar: arma un único mail por
    /// confirmación con el detalle de todas sus compras (con un PDF por cartón) o un aviso simple
    /// de cancelación (sin adjuntos), según <c>TipoEnvio</c>. Una falla en un envío no aborta el
    /// resto del batch — cada envío está envuelto en su propio try/catch (mitigación R-04 del
    /// threat model).
    /// </summary>
    Task ProcesarPendientesAsync();
}
