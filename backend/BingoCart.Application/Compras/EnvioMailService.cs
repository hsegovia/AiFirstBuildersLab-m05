using System.Globalization;
using System.Net;
using System.Text;
using BingoCart.Application.Compras.Dtos;
using BingoCart.Domain.Compras;
using Microsoft.Extensions.Logging;

namespace BingoCart.Application.Compras;

/// <summary>
/// Orquesta el outbox de mail de confirmación de compra (spec FEAT-009b, Block 2). Combina los 3
/// puertos nuevos (<see cref="IEnvioMailRepository"/>, <see cref="IEmailSender"/>,
/// <see cref="ICartonPdfRenderer"/>) sin conocer ninguna implementación concreta — Infrastructure
/// (Block 3) es quien sabe de EF Core/MailKit/QuestPDF.
/// </summary>
public sealed class EnvioMailService : IEnvioMailService
{
    private readonly IEnvioMailRepository _envioMailRepository;
    private readonly IEmailSender _emailSender;
    private readonly ICartonPdfRenderer _cartonPdfRenderer;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EnvioMailService> _logger;

    public EnvioMailService(
        IEnvioMailRepository envioMailRepository,
        IEmailSender emailSender,
        ICartonPdfRenderer cartonPdfRenderer,
        TimeProvider timeProvider,
        ILogger<EnvioMailService> logger)
    {
        _envioMailRepository = envioMailRepository;
        _emailSender = emailSender;
        _cartonPdfRenderer = cartonPdfRenderer;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task EncolarConfirmacionAsync(Guid confirmacionId, Guid compradorId)
    {
        var ahoraUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var envio = EnvioMail.CrearConfirmacion(confirmacionId, compradorId, ahoraUtc);
        await _envioMailRepository.EncolarAsync(envio);
    }

    public async Task EncolarCancelacionAsync(Guid compraId, Guid compradorId)
    {
        var ahoraUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var envio = EnvioMail.CrearCancelacion(compraId, compradorId, ahoraUtc);
        await _envioMailRepository.EncolarAsync(envio);
    }

    public async Task ProcesarPendientesAsync()
    {
        var ahoraUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var pendientes = await _envioMailRepository.ObtenerPendientesAsync(ahoraUtc);

        // Cada envío en su propio try/catch: una falla no aborta el resto del batch (mitigación
        // R-04 del threat model, primera capa — la segunda capa vive en el BackgroundService de
        // Block 3).
        foreach (var envio in pendientes)
        {
            try
            {
                EnvioMailMensaje? mensaje;

                // FEAT-009c, Block 2: ramificación real por TipoEnvio (ya no un `!` interino —
                // ConfirmacionId/CompraId son Guid? porque cada TipoEnvio setea solo uno de los dos;
                // el `!` de cada rama está justificado por esa invariante, garantizada por
                // EnvioMail.CrearConfirmacion/CrearCancelacion).
                if (envio.TipoEnvio == TipoEnvioMail.Confirmacion)
                {
                    var datos = await _envioMailRepository.ObtenerDatosParaEnviarAsync(envio.ConfirmacionId!.Value);
                    if (datos is null)
                    {
                        // Caso esperable, no una excepción (ver "Error handling" del spec): se
                        // saltea sin marcar Fallido, el envío queda Pendiente por si el dato
                        // reaparece.
                        _logger.LogWarning(
                            "No se encontraron datos para el envio {EnvioId} de la confirmacion {ConfirmacionId}; se saltea.",
                            envio.Id,
                            envio.ConfirmacionId);
                        continue;
                    }

                    mensaje = ArmarMensaje(datos);
                }
                else
                {
                    var datos = await _envioMailRepository.ObtenerDatosParaCancelacionAsync(envio.CompraId!.Value);
                    if (datos is null)
                    {
                        _logger.LogWarning(
                            "No se encontraron datos para el envio {EnvioId} de la compra {CompraId}; se saltea.",
                            envio.Id,
                            envio.CompraId);
                        continue;
                    }

                    mensaje = ArmarMensajeCancelacion(datos);
                }

                await _emailSender.EnviarAsync(mensaje);

                envio.RegistrarExito();
                await _envioMailRepository.ActualizarAsync(envio);
            }
            catch (Exception ex)
            {
                envio.RegistrarIntentoFallido(ahoraUtc);
                await _envioMailRepository.ActualizarAsync(envio);

                // R-01/R-02 (HIGH, threat model): ÚNICAMENTE tipo de excepción + IDs opacos. NUNCA
                // ex.Message, NUNCA PII del comprador ni contenido del mensaje.
                _logger.LogWarning(
                    "Fallo el intento de envio {EnvioId} (tipo {TipoEnvio}). Tipo de excepcion: {TipoExcepcion}.",
                    envio.Id,
                    envio.TipoEnvio,
                    ex.GetType().Name);
            }
        }
    }

    private EnvioMailMensaje ArmarMensaje(DatosParaMailConfirmacion datos)
    {
        var adjuntos = new List<AdjuntoMail>();
        foreach (var compra in datos.Compras)
        {
            foreach (var carton in compra.Cartones)
            {
                var pdf = _cartonPdfRenderer.Renderizar(carton.CartonId, carton.NumeroCorrelativo, carton.Numeros);
                adjuntos.Add(new AdjuntoMail($"{carton.CartonId}.pdf", pdf));
            }
        }

        return new EnvioMailMensaje(
            datos.MailComprador,
            "Confirmación de tu compra",
            ArmarCuerpoHtml(datos),
            adjuntos);
    }

    /// <summary>
    /// Arma el detalle en HTML de todas las compras de la confirmación (AC-02): nombre de
    /// organización, ID de compra, monto total y números de cada cartón, por cada compra. Cada
    /// cartón se identifica por su número correlativo dentro del bingo, no por su GUID (FEAT-009d,
    /// Block 2 — FR-12/AC-14): el mail era la última superficie del recorrido donde el comprador
    /// veía un identificador que no le dice nada, en vez del número que ya vio en el descubrimiento,
    /// en el carrito y en la confirmación. El GUID sigue estando en el PDF adjunto, que es donde
    /// hace falta (RF-06). Los
    /// campos que provienen de datos ingresados por el usuario (nombre del comprador, nombre de
    /// organización) se escapan con <see cref="WebUtility.HtmlEncode"/> — nunca se interpolan
    /// crudos en el HTML, mismo criterio de "nunca concatenación manual insegura" que Infrastructure
    /// aplica del lado de MimeKit (R-07 del threat model).
    /// </summary>
    private static string ArmarCuerpoHtml(DatosParaMailConfirmacion datos)
    {
        var sb = new StringBuilder();
        sb.Append("<p>Hola ").Append(WebUtility.HtmlEncode(datos.NombreComprador))
            .Append(", gracias por tu compra.</p>");

        foreach (var compra in datos.Compras)
        {
            sb.Append("<h3>").Append(WebUtility.HtmlEncode(compra.NombreOrganizacion)).Append("</h3>");
            sb.Append("<p>Compra ").Append(compra.CompraId)
                .Append(" — Total: ").Append(compra.MontoTotal.ToString("F2", CultureInfo.InvariantCulture))
                .Append("</p>");
            sb.Append("<ul>");
            foreach (var carton in compra.Cartones)
            {
                sb.Append("<li>Cartón N° ").Append(carton.NumeroCorrelativo)
                    .Append(": ").Append(string.Join(", ", carton.Numeros))
                    .Append("</li>");
            }

            sb.Append("</ul>");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Arma el mensaje de cancelación (spec FEAT-009c, Block 2): un aviso simple, SIN adjuntos PDF
    /// (no hay cartones que confirmar, se están liberando). <see cref="WebUtility.HtmlEncode"/>
    /// aplicado a <c>NombreComprador</c>/<c>NombreOrganizacion</c> antes de interpolarlos en el
    /// cuerpo HTML — mismo mitigation que R-07 del threat model de FEAT-009b (<see cref="ArmarCuerpoHtml"/>):
    /// son campos suministrados por el comprador/organizador, y el cuerpo del mail es HTML.
    /// </summary>
    private static EnvioMailMensaje ArmarMensajeCancelacion(DatosParaMailCancelacion datos)
    {
        var sb = new StringBuilder();
        sb.Append("<p>Hola ").Append(WebUtility.HtmlEncode(datos.NombreComprador))
            .Append(", tu compra en ").Append(WebUtility.HtmlEncode(datos.NombreOrganizacion))
            .Append(" fue cancelada.</p>");
        sb.Append("<p>Compra ").Append(datos.CompraId).Append("</p>");

        return new EnvioMailMensaje(
            datos.MailComprador,
            "Tu compra fue cancelada",
            sb.ToString(),
            Array.Empty<AdjuntoMail>());
    }
}
