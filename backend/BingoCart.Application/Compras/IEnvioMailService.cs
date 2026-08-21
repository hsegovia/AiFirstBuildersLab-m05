namespace BingoCart.Application.Compras;

/// <summary>
/// Servicio de aplicación para el outbox de mail de confirmación de compra (spec FEAT-009b,
/// Block 2).
/// </summary>
public interface IEnvioMailService
{
    /// <summary>
    /// Encola un envío en estado Pendiente para la confirmación <paramref name="confirmacionId"/>
    /// del comprador <paramref name="compradorId"/> (FR-02).
    /// </summary>
    Task EncolarAsync(Guid confirmacionId, Guid compradorId);

    /// <summary>
    /// Procesa todos los envíos pendientes listos para (re)intentar: arma un único mail por
    /// confirmación con el detalle de todas sus compras, genera un PDF por cartón y lo envía. Una
    /// falla en un envío no aborta el resto del batch — cada envío está envuelto en su propio
    /// try/catch (mitigación R-04 del threat model).
    /// </summary>
    Task ProcesarPendientesAsync();
}
