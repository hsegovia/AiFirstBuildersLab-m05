using System.Net;
using System.Text.Json;
using BingoCart.Domain.Auth.Exceptions;
using BingoCart.Domain.Bingos.Exceptions;
using BingoCart.Domain.Carritos.Exceptions;
using BingoCart.Domain.Common;
using BingoCart.Domain.Compras.Exceptions;
using BingoCart.Domain.Organizadores.Exceptions;
// Ambos bounded contexts (Organizadores/Compradores) tienen sus propias excepciones con el MISMO
// nombre de clase (CuitInvalidoException/MailYaRegistradoException/PasswordInvalidaException,
// decisión de PLAN spec FEAT-009a: "cada bounded context tiene sus propias excepciones") — alias
// explícitos para poder tener un catch de cada una sin ambigüedad de compilación.
using OrganizadorCuitInvalidoException = BingoCart.Domain.Organizadores.Exceptions.CuitInvalidoException;
using OrganizadorMailYaRegistradoException = BingoCart.Domain.Organizadores.Exceptions.MailYaRegistradoException;
using OrganizadorPasswordInvalidaException = BingoCart.Domain.Organizadores.Exceptions.PasswordInvalidaException;
using CompradorCuitInvalidoException = BingoCart.Domain.Compradores.Exceptions.CuitInvalidoException;
using CompradorMailYaRegistradoException = BingoCart.Domain.Compradores.Exceptions.MailYaRegistradoException;
using CompradorPasswordInvalidaException = BingoCart.Domain.Compradores.Exceptions.PasswordInvalidaException;

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
        catch (OrganizadorCuitInvalidoException ex)
        {
            await ManejarExcepcionDeDominioAsync(context, ex, HttpStatusCode.BadRequest, "CuitInvalido");
        }
        catch (TelefonoInvalidoException ex)
        {
            await ManejarExcepcionDeDominioAsync(context, ex, HttpStatusCode.BadRequest, "TelefonoInvalido");
        }
        catch (OrganizadorPasswordInvalidaException ex)
        {
            await ManejarExcepcionDeDominioAsync(context, ex, HttpStatusCode.BadRequest, "PasswordInvalida");
        }
        catch (OrganizadorMailYaRegistradoException ex)
        {
            await ManejarExcepcionDeDominioAsync(context, ex, HttpStatusCode.Conflict, "MailYaRegistrado");
        }
        // Excepciones de comprador (spec FEAT-009a, Block 3) — mismo error/status que sus
        // equivalentes de organizador arriba, distinto tipo concreto (alias de using).
        catch (CompradorCuitInvalidoException ex)
        {
            await ManejarExcepcionDeDominioAsync(context, ex, HttpStatusCode.BadRequest, "CuitInvalido");
        }
        catch (CompradorPasswordInvalidaException ex)
        {
            await ManejarExcepcionDeDominioAsync(context, ex, HttpStatusCode.BadRequest, "PasswordInvalida");
        }
        catch (CompradorMailYaRegistradoException ex)
        {
            await ManejarExcepcionDeDominioAsync(context, ex, HttpStatusCode.Conflict, "MailYaRegistrado");
        }
        catch (CarritoVacioException ex)
        {
            await ManejarExcepcionDeDominioAsync(context, ex, HttpStatusCode.BadRequest, "CarritoVacio");
        }
        catch (ReservaCarritoInvalidaException ex)
        {
            // Único catch que agrega un campo extra al body de error (cartonIdsInvalidos, spec
            // FEAT-009a Block 3, API contract) — el resto de los catches de esta clase solo
            // necesita {error, message}, por eso no comparten ManejarExcepcionDeDominioAsync acá.
            _logger.LogWarning(
                "Excepción de dominio. Tipo: {ExceptionType}. CorrelationId: {CorrelationId}",
                ex.GetType().Name,
                context.TraceIdentifier);

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            var body = JsonSerializer.Serialize(new
            {
                error = "ReservaCarritoInvalida",
                message = ex.Message,
                cartonIdsInvalidos = ex.CartonIdsInvalidos
            });
            await context.Response.WriteAsync(body);
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
        catch (CartonInexistenteException ex)
        {
            await ManejarExcepcionDeDominioAsync(context, ex, HttpStatusCode.NotFound, "CartonInexistente");
        }
        catch (CartonYaReservadoException ex)
        {
            await ManejarExcepcionDeDominioAsync(context, ex, HttpStatusCode.Conflict, "CartonYaReservado");
        }
        catch (CantidadDescartadosExcedeLimiteException ex)
        {
            await ManejarExcepcionDeDominioAsync(context, ex, HttpStatusCode.BadRequest, "CantidadDescartadosExcedeLimite");
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
