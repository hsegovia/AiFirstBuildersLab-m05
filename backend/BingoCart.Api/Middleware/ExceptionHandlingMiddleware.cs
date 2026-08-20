using System.Net;
using System.Text.Json;
using BingoCart.Domain.Auth.Exceptions;
using BingoCart.Domain.Bingos.Exceptions;
using BingoCart.Domain.Common;
using BingoCart.Domain.Organizadores.Exceptions;

namespace BingoCart.Api.Middleware;

/// <summary>
/// Middleware global de manejo de excepciones (Block 4 del spec FEAT-001a). Traduce las
/// excepciones de dominio de <c>Organizador</c> a respuestas HTTP consistentes y captura cualquier
/// excepción no controlada devolviendo un 500 genérico, sin filtrar stack traces ni datos
/// personales. El log interno de cada excepción nunca incluye CUIT, mail ni teléfono — solo el tipo
/// de excepción y el <see cref="HttpContext.TraceIdentifier"/> como correlation id (NFR-02).
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private const string MensajeErrorInterno = "Ocurrió un error interno. Intentá nuevamente más tarde.";

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (CuitInvalidoException ex)
        {
            await ManejarExcepcionDeDominioAsync(context, ex, HttpStatusCode.BadRequest, "CuitInvalido");
        }
        catch (TelefonoInvalidoException ex)
        {
            await ManejarExcepcionDeDominioAsync(context, ex, HttpStatusCode.BadRequest, "TelefonoInvalido");
        }
        catch (PasswordInvalidaException ex)
        {
            await ManejarExcepcionDeDominioAsync(context, ex, HttpStatusCode.BadRequest, "PasswordInvalida");
        }
        catch (MailYaRegistradoException ex)
        {
            await ManejarExcepcionDeDominioAsync(context, ex, HttpStatusCode.Conflict, "MailYaRegistrado");
        }
        catch (CredencialesInvalidasException ex)
        {
            await ManejarExcepcionDeDominioAsync(context, ex, HttpStatusCode.Unauthorized, "CredencialesInvalidas");
        }
        catch (FechaSorteoInvalidaException ex)
        {
            await ManejarExcepcionDeDominioAsync(context, ex, HttpStatusCode.BadRequest, "FechaSorteoInvalida");
        }
        catch (CantidadCartonesExcedeLimiteException ex)
        {
            await ManejarExcepcionDeDominioAsync(context, ex, HttpStatusCode.BadRequest, "CantidadCartonesExcedeLimite");
        }
        catch (CantidadCartonesInvalidaException ex)
        {
            await ManejarExcepcionDeDominioAsync(context, ex, HttpStatusCode.BadRequest, "CantidadCartonesInvalida");
        }
        catch (CostoPorCartonInvalidoException ex)
        {
            await ManejarExcepcionDeDominioAsync(context, ex, HttpStatusCode.BadRequest, "CostoPorCartonInvalido");
        }
        catch (BingoActivoExistenteException ex)
        {
            await ManejarExcepcionDeDominioAsync(context, ex, HttpStatusCode.Conflict, "BingoActivoExistente");
        }
        catch (BingoNoEncontradoException ex)
        {
            await ManejarExcepcionDeDominioAsync(context, ex, HttpStatusCode.NotFound, "BingoNoEncontrado");
        }
        catch (BingoConComprasException ex)
        {
            await ManejarExcepcionDeDominioAsync(context, ex, HttpStatusCode.Conflict, "BingoConCompras");
        }
        catch (OrganizadorNoEncontradoException ex)
        {
            await ManejarExcepcionDeDominioAsync(context, ex, HttpStatusCode.NotFound, "OrganizadorNoEncontrado");
        }
        catch (Exception ex)
        {
            // No controlada: nunca se expone el mensaje real (puede contener detalles internos) ni
            // datos personales. El log SÍ registra el tipo de excepción y el correlation id, nunca
            // CUIT/mail/teléfono (que tampoco están disponibles en este punto genérico).
            _logger.LogError(
                ex,
                "Excepción no controlada. Tipo: {ExceptionType}. CorrelationId: {CorrelationId}",
                ex.GetType().Name,
                context.TraceIdentifier);

            await EscribirRespuestaAsync(
                context,
                HttpStatusCode.InternalServerError,
                "ErrorInterno",
                MensajeErrorInterno);
        }
    }

    private async Task ManejarExcepcionDeDominioAsync(
        HttpContext context,
        DomainException ex,
        HttpStatusCode statusCode,
        string error)
    {
        _logger.LogWarning(
            "Excepción de dominio. Tipo: {ExceptionType}. CorrelationId: {CorrelationId}",
            ex.GetType().Name,
            context.TraceIdentifier);

        await EscribirRespuestaAsync(context, statusCode, error, ex.Message);
    }

    private static async Task EscribirRespuestaAsync(
        HttpContext context,
        HttpStatusCode statusCode,
        string error,
        string message)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var body = JsonSerializer.Serialize(new { error, message });
        await context.Response.WriteAsync(body);
    }
}
