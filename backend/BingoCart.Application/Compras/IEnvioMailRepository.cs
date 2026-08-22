using BingoCart.Application.Compras.Dtos;
using BingoCart.Domain.Compras;

namespace BingoCart.Application.Compras;

/// <summary>
/// Puerto de persistencia del outbox de <see cref="EnvioMail"/> (spec FEAT-009b, Block 2).
/// Infraestructura pura — no decide negocio (la máquina de estados de reintentos vive en
/// <see cref="EnvioMail"/>, Domain/Block 1). Infrastructure lo implementa en Block 3 vía EF Core.
/// </summary>
public interface IEnvioMailRepository
{
    /// <summary>
    /// Persiste un nuevo envío en estado <see cref="EstadoEnvioMail.Pendiente"/>.
    /// </summary>
    Task EncolarAsync(EnvioMail envio);

    /// <summary>
    /// Devuelve los envíos listos para procesar en <paramref name="ahoraUtc"/>: en estado
    /// <see cref="EstadoEnvioMail.Pendiente"/> y sin <see cref="EnvioMail.ProximoIntentoUtc"/>
    /// programado, o con uno ya vencido.
    /// </summary>
    Task<IReadOnlyList<EnvioMail>> ObtenerPendientesAsync(DateTime ahoraUtc);

    /// <summary>
    /// Resuelve los datos del comprador y de todas las <c>Compra</c> con el
    /// <paramref name="confirmacionId"/> dado, para armar el mail agrupado (AC-01/AC-02). Devuelve
    /// <c>null</c> si no hay ninguna — caso defensivo, el dato pudo desaparecer entre encolar y
    /// procesar (ningún flujo actual del sistema lo provoca hoy).
    /// </summary>
    Task<DatosParaMailConfirmacion?> ObtenerDatosParaEnviarAsync(Guid confirmacionId);

    /// <summary>
    /// Persiste el estado ya actualizado de <paramref name="envio"/> (éxito o intento fallido).
    /// </summary>
    Task ActualizarAsync(EnvioMail envio);
}
