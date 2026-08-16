using System.Text;
using System.Text.Json;
using BingoCart.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace BingoCart.Api.Tests.Middleware;

/// <summary>
/// Test de middleware AISLADO (Block 4 del spec FEAT-001a): no pasa por
/// <c>WebApplicationFactory</c> ni por la base de datos, solo un <see cref="RequestDelegate"/> que
/// lanza una excepción genérica directamente. Verifica el mapeo a 500 genérico sin fugar detalles
/// internos (stack trace, mensaje de la excepción) en el body de la respuesta (NFR-02).
/// </summary>
public sealed class ExceptionHandlingMiddlewareTests
{
    [Fact]
    public async Task Invoke_AnteExcepcionNoControlada_Devuelve500ErrorInternoSinDetalles()
    {
        const string mensajeInterno = "Detalle interno sensible: conexión rechazada por el servidor XYZ";

        RequestDelegate next = _ => throw new InvalidOperationException(mensajeInterno);
        var middleware = new ExceptionHandlingMiddleware(next, NullLogger<ExceptionHandlingMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync();

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.DoesNotContain(mensajeInterno, body);
        Assert.DoesNotContain(nameof(InvalidOperationException), body);
        Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" at ", body);

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var parsed = JsonSerializer.Deserialize<ErrorResponseDto>(body, options);

        Assert.NotNull(parsed);
        Assert.Equal("ErrorInterno", parsed!.Error);
        Assert.False(string.IsNullOrWhiteSpace(parsed.Message));
    }

    private sealed record ErrorResponseDto(string Error, string Message);
}
