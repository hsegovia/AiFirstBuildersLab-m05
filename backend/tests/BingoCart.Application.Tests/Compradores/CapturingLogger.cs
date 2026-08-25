using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace BingoCart.Application.Tests.Compradores;

/// <summary>
/// Logger de prueba (helper compartido de test, no parte del spec) que captura el texto YA
/// FORMATEADO de cada log emitido — mismo helper que
/// <c>BingoCart.Infrastructure.Tests.Notificaciones.CapturingLogger&lt;T&gt;</c> (spec FEAT-009b,
/// Block 3), copiado acá porque <c>BingoCart.Application.Tests</c> no referencia
/// <c>BingoCart.Infrastructure.Tests</c>. Necesario para
/// <c>CompradorServiceTests.ActualizarCuentaAsync_Exitosa_RegistraAuditoriaConNombresDeCamposYSinValores</c>
/// (spec FEAT-009d, Block 5): verificar directamente que el log de auditoría lleva
/// <c>compradorId</c>/timestamp/nombres de campo y NO ningún valor de PII ni la contraseña requiere
/// inspeccionar el string final, no solo si se invocó <c>LogInformation</c>.
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
