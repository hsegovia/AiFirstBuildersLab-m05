using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using BingoCart.Application.Compras.Dtos;
using BingoCart.Infrastructure.Notificaciones;
using Microsoft.Extensions.Options;

namespace BingoCart.Infrastructure.Tests.Notificaciones;

/// <summary>
/// Tests de integración de <see cref="MailKitEmailSender"/> (spec FEAT-009b, Block 3) contra el
/// <c>smtp4dev</c> real de <c>docker-compose.yml</c> (puerto 2525, StartTls habilitado vía
/// <c>ServerOptions__TlsMode</c>) — sin mocks, mismo criterio "SQL Server/Redis reales" ya aplicado
/// en el resto de <c>BingoCart.Infrastructure.Tests</c>.
/// </summary>
public sealed class MailKitEmailSenderTests : IAsyncLifetime
{
    // Mismo puerto remapeado que docker-compose.yml (2525:25) — desarrollo/test local únicamente.
    private const string SmtpHost = "localhost";
    private const int SmtpPort = 2525;
    private const string ApiMensajesUrl = "http://localhost:5080/api/messages";

    private static readonly HttpClient HttpClient = new();

    // Rule #0 (testing.instructions.md): un test que crea datos los limpia en su propio teardown.
    // Solo el mail que este test efectivamente entregó a smtp4dev se borra — nunca el inbox
    // completo, que podría contener mensajes de otra corrida concurrente.
    private readonly List<string> _idsMensajesCreados = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var id in _idsMensajesCreados)
        {
            using var respuesta = await HttpClient.DeleteAsync($"{ApiMensajesUrl}/{id}");
        }
    }

    // AllowInvalidCertificate=true SOLO porque smtp4dev usa un certificado autofirmado generado al
    // vuelo (sin CA de confianza) — desviación documentada del spec (ver reporte del bloque): la
    // config real de Producción nunca fija esta clave (queda ausente => false por defecto),
    // mitigación R-08 (smtp4dev nunca es el Smtp:Host de Producción).
    private static SmtpSettings NuevaConfiguracion(string host = SmtpHost, int port = SmtpPort) => new()
    {
        Host = host,
        Port = port,
        User = string.Empty,
        Password = string.Empty,
        From = "no-reply@bingocart.local",
        AllowInvalidCertificate = true,
    };

    private static MailKitEmailSender NuevoSender(SmtpSettings settings, CapturingLogger<MailKitEmailSender> logger) =>
        new(Options.Create(settings), logger);

    [Fact]
    public async Task EnviarAsync_ConSmtpReal_LlegaElMailConAsuntoYAdjuntos()
    {
        var asuntoUnico = $"Confirmación de compra {Guid.NewGuid()}";
        var mensaje = new EnvioMailMensaje(
            "comprador@bingocart.local",
            asuntoUnico,
            "<p>Hola, gracias por tu compra.</p>",
            new[]
            {
                new AdjuntoMail("carton-uno.pdf", new byte[] { 0x25, 0x50, 0x44, 0x46 }),
                new AdjuntoMail("carton-dos.pdf", new byte[] { 0x25, 0x50, 0x44, 0x46 }),
            });

        var logger = new CapturingLogger<MailKitEmailSender>();
        var sender = NuevoSender(NuevaConfiguracion(), logger);

        await sender.EnviarAsync(mensaje);

        var mensajeRecibido = await BuscarMensajePorAsuntoAsync(asuntoUnico);
        _idsMensajesCreados.Add(mensajeRecibido.GetProperty("id").GetString()!);

        Assert.Equal(asuntoUnico, mensajeRecibido.GetProperty("subject").GetString());
        Assert.Equal(2, mensajeRecibido.GetProperty("attachmentCount").GetInt32());
    }

    [Fact]
    public async Task EnviarAsync_ConFallaDeConexion_NuncaLogueaExMessage()
    {
        // Puerto sin nada escuchando: falla de conexión inmediata (ECONNREFUSED), mismo camino de
        // código que un timeout de 30s (ambos son una excepción de MailKit propagada desde
        // SmtpClient — un solo test cubre ambos, per spec).
        var configuracionInvalida = NuevaConfiguracion(port: 19999);
        var logger = new CapturingLogger<MailKitEmailSender>();
        var sender = NuevoSender(configuracionInvalida, logger);

        var mensaje = new EnvioMailMensaje(
            "comprador@bingocart.local",
            "No debería loguear esto",
            "<p>Contenido sensible que NUNCA debe aparecer en el log.</p>",
            Array.Empty<AdjuntoMail>());

        var excepcion = await Assert.ThrowsAnyAsync<Exception>(() => sender.EnviarAsync(mensaje));

        Assert.NotEmpty(logger.MensajesCapturados);
        Assert.All(logger.MensajesCapturados, texto =>
        {
            Assert.DoesNotContain(excepcion.Message, texto, StringComparison.Ordinal);
            Assert.DoesNotContain(mensaje.Asunto, texto, StringComparison.Ordinal);
            Assert.DoesNotContain(mensaje.CuerpoHtml, texto, StringComparison.Ordinal);
            Assert.DoesNotContain(mensaje.Destinatario, texto, StringComparison.Ordinal);
        });
        Assert.Contains(logger.MensajesCapturados, texto => texto.Contains(excepcion.GetType().Name, StringComparison.Ordinal));
    }

    private static async Task<JsonElement> BuscarMensajePorAsuntoAsync(string asunto)
    {
        // Polling corto: smtp4dev entrega el mail casi instantáneamente, pero no hay garantía
        // síncrona — mismo criterio que cualquier test de integración contra un proceso externo.
        for (var intento = 0; intento < 20; intento++)
        {
            var respuesta = await HttpClient.GetFromJsonAsync<JsonElement>(ApiMensajesUrl);
            var resultados = respuesta.GetProperty("results").EnumerateArray();
            var encontrado = resultados.FirstOrDefault(m => m.GetProperty("subject").GetString() == asunto);
            if (encontrado.ValueKind != JsonValueKind.Undefined)
            {
                return encontrado;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException($"El mail con asunto '{asunto}' nunca llegó a smtp4dev.");
    }
}
