using BingoCart.Application.Compras;
using BingoCart.Application.Compras.Dtos;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace BingoCart.Infrastructure.Notificaciones;

/// <summary>
/// Implementa <see cref="IEmailSender"/> (Application, Block 2 del spec FEAT-009b) vía MailKit.
/// </summary>
public sealed class MailKitEmailSender : IEmailSender
{
    private const int TimeoutMs = 30_000; // R-05: 30s, evita que un SMTP colgado bloquee el batch.

    private readonly SmtpSettings _settings;
    private readonly ILogger<MailKitEmailSender> _logger;

    public MailKitEmailSender(IOptions<SmtpSettings> settings, ILogger<MailKitEmailSender> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task EnviarAsync(EnvioMailMensaje mensaje)
    {
        var mimeMessage = new MimeMessage();
        mimeMessage.From.Add(MailboxAddress.Parse(_settings.From));
        mimeMessage.To.Add(MailboxAddress.Parse(mensaje.Destinatario));
        mimeMessage.Subject = mensaje.Asunto;

        // BodyBuilder (API segura de MimeKit, mitigación R-07) en vez de concatenación manual de
        // strings. mensaje.CuerpoHtml ya viene HTML-encoded desde EnvioMailService (Application,
        // Block 2) — se asigna TAL CUAL a HtmlBody: BodyBuilder no re-escapa su input, un segundo
        // encode acá corrompería el mail (&amp; -> &amp;amp;).
        var bodyBuilder = new BodyBuilder { HtmlBody = mensaje.CuerpoHtml };
        foreach (var adjunto in mensaje.Adjuntos)
        {
            bodyBuilder.Attachments.Add(adjunto.NombreArchivo, adjunto.Contenido, new ContentType("application", "pdf"));
        }

        mimeMessage.Body = bodyBuilder.ToMessageBody();

        using var client = new SmtpClient
        {
            Timeout = TimeoutMs,
        };

        if (_settings.AllowInvalidCertificate)
        {
            // Únicamente para el smtp4dev de desarrollo/test (certificado autofirmado, ver
            // SmtpSettings.AllowInvalidCertificate) — nunca true contra un SMTP real (R-08).
            client.ServerCertificateValidationCallback = (_, _, _, _) => true;
        }

        try
        {
            // StartTls (no StartTlsWhenAvailable): TLS obligatorio, mitigación R-03 — si el
            // servidor no lo soporta, ConnectAsync lanza en vez de degradar a texto plano.
            await client.ConnectAsync(_settings.Host, _settings.Port, SecureSocketOptions.StartTls);

            if (!string.IsNullOrEmpty(_settings.User))
            {
                await client.AuthenticateAsync(_settings.User, _settings.Password);
            }

            await client.SendAsync(mimeMessage);
            await client.DisconnectAsync(quit: true);
        }
        catch (Exception ex)
        {
            // R-01 (HIGH, threat model): ÚNICAMENTE el tipo de excepción. NUNCA ex.Message (puede
            // incluir credenciales/detalles del servidor), NUNCA el destinatario, asunto o cuerpo
            // del mensaje. Relanza para que EnvioMailService (Application, Block 2) registre el
            // intento fallido.
            _logger.LogWarning(
                "Fallo el envio de mail via SMTP. Tipo de excepcion: {TipoExcepcion}.",
                ex.GetType().Name);
            throw;
        }
    }
}
