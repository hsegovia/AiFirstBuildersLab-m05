using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace BingoCart.Infrastructure.Tests.Notificaciones;

/// <summary>
/// Logger de prueba (helper compartido de test, no parte del spec) que captura el texto YA
/// FORMATEADO de cada log emitido — necesario para <c>MailKitEmailSenderTests</c>/
/// <c>EnvioMailBackgroundServiceTests</c> (spec FEAT-009b, Block 3): verificar directamente que
/// ningún log contiene <c>ex.Message</c> ni PII (R-01/R-02 del threat model) requiere inspeccionar
/// el string final, no solo si se invocó <c>LogWarning</c>.
/// </summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public ConcurrentQueue<string> MensajesCapturados { get; } = new();

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        MensajesCapturados.Enqueue(formatter(state, exception));
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
