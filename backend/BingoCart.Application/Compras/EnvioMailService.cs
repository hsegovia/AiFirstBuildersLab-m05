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

    public async Task EncolarAsync(Guid confirmacionId, Guid compradorId)
    {
        var ahoraUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var envio = EnvioMail.Crear(confirmacionId, compradorId, ahoraUtc);
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
                var datos = await _envioMailRepository.ObtenerDatosParaEnviarAsync(envio.ConfirmacionId);
                if (datos is null)
                {
                    // Caso esperable, no una excepción (ver "Error handling" del spec): se saltea
                    // sin marcar Fallido, el envío queda Pendiente por si el dato reaparece.
                    _logger.LogWarning(
                        "No se encontraron datos para el envio {EnvioId} de la confirmacion {ConfirmacionId}; se saltea.",
                        envio.Id,
                        envio.ConfirmacionId);
                    continue;
                }

                var mensaje = ArmarMensaje(datos);
                await _emailSender.EnviarAsync(mensaje);

                envio.RegistrarExito();
                await _envioMailRepository.ActualizarAsync(envio);
            }
            catch (Exception ex)
            {
                envio.RegistrarIntentoFallido(ahoraUtc);
                await _envioMailRepository.ActualizarAsync(envio);

                // R-01/R-02 (HIGH, threat model): ÚNICAMENTE tipo de excepción + IDs de envío. NUNCA
                // ex.Message, NUNCA PII del comprador ni contenido del mensaje.
                _logger.LogWarning(
                    "Fallo el intento de envio {EnvioId} de la confirmacion {ConfirmacionId}. Tipo de excepcion: {TipoExcepcion}.",
                    envio.Id,
                    envio.ConfirmacionId,
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
                var pdf = _cartonPdfRenderer.Renderizar(carton.CartonId, carton.Numeros);
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
    /// organización, ID de compra, monto total y números de cada cartón, por cada compra. Los
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
                sb.Append("<li>Cartón ").Append(carton.CartonId)
                    .Append(": ").Append(string.Join(", ", carton.Numeros))
                    .Append("</li>");
            }

            sb.Append("</ul>");
        }

        return sb.ToString();
    }
}
