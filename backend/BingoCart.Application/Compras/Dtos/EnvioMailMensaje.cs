namespace BingoCart.Application.Compras.Dtos;

/// <summary>
/// Mensaje de mail ya armado, listo para <c>IEmailSender.EnviarAsync</c> (spec FEAT-009b, Block 2).
/// Nombrado <c>EnvioMailMensaje</c> — NO <c>MailMessage</c> — para no colisionar con
/// <see cref="System.Net.Mail.MailMessage"/> del BCL, y para mantener la convención en español ya
/// establecida en esta carpeta.
/// </summary>
public sealed record EnvioMailMensaje(
    string Destinatario,
    string Asunto,
    // Ya viene HTML-encoded (WebUtility.HtmlEncode aplicado a los campos de usuario en
    // EnvioMailService.ArmarCuerpoHtml, mitigación R-07). MailKitEmailSender (Block 3) debe asignarlo
    // a BodyBuilder.HtmlBody tal cual — BodyBuilder no re-escapa su input, así que un segundo
    // HtmlEncode acá corrompería el mail (&amp; → &amp;amp;).
    string CuerpoHtml,
    IReadOnlyList<AdjuntoMail> Adjuntos);

/// <summary>
/// Adjunto binario del mail de confirmación — un PDF por cartón comprado (AC-03).
/// </summary>
public sealed record AdjuntoMail(string NombreArchivo, byte[] Contenido);
