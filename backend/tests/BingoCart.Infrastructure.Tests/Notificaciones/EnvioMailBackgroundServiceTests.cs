using System;
using System.Threading;
using System.Threading.Tasks;
using BingoCart.Application.Compras;
using BingoCart.Infrastructure.Notificaciones;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace BingoCart.Infrastructure.Tests.Notificaciones;

/// <summary>
/// Tests unitarios de <see cref="EnvioMailBackgroundService"/> (spec FEAT-009b, Block 3) —
/// <see cref="IEnvioMailService"/> mockeado (puerto de Application, no la unidad bajo prueba),
/// intervalo corto inyectado explícitamente para no depender del minuto real de NFR-01. El mock se
/// registra en un <see cref="IServiceScopeFactory"/> real (no un fake a mano): el propio
/// <see cref="EnvioMailBackgroundService"/> crea un <see cref="IServiceScope"/> por tick (ver su
/// comentario de clase — Singleton no puede consumir un IEnvioMailService Scoped directo).
/// </summary>
public sealed class EnvioMailBackgroundServiceTests
{
    private static readonly TimeSpan IntervaloDeTest = TimeSpan.FromMilliseconds(20);

    private static IServiceScopeFactory NuevaScopeFactory(IEnvioMailService envioMailService)
    {
        var servicios = new ServiceCollection();
        servicios.AddScoped(_ => envioMailService);
        return servicios.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    [Fact]
    public async Task ExecuteAsync_InvocaProcesarPendientesAsyncEnCadaTick()
    {
        var contador = 0;
        var segundoTick = new TaskCompletionSource();
        var envioMailServiceMock = new Mock<IEnvioMailService>();
        envioMailServiceMock
            .Setup(s => s.ProcesarPendientesAsync())
            .Returns(Task.CompletedTask)
            .Callback(() =>
            {
                if (Interlocked.Increment(ref contador) >= 2)
                {
                    segundoTick.TrySetResult();
                }
            });

        var servicio = new EnvioMailBackgroundService(
            NuevaScopeFactory(envioMailServiceMock.Object),
            new CapturingLogger<EnvioMailBackgroundService>(),
            IntervaloDeTest);

        await servicio.StartAsync(CancellationToken.None);
        var completado = await Task.WhenAny(segundoTick.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        await servicio.StopAsync(CancellationToken.None);

        Assert.Same(segundoTick.Task, completado);
        Assert.True(contador >= 2);
        envioMailServiceMock.Verify(s => s.ProcesarPendientesAsync(), Times.AtLeast(2));
    }

    [Fact]
    public async Task ExecuteAsync_ConProcesarPendientesAsyncLanzandoExcepcion_SigueVivoYReintentaEnElProximoTick()
    {
        var contador = 0;
        var tercerTick = new TaskCompletionSource();
        var envioMailServiceMock = new Mock<IEnvioMailService>();
        envioMailServiceMock
            .Setup(s => s.ProcesarPendientesAsync())
            .Returns(() =>
            {
                var intentoActual = Interlocked.Increment(ref contador);
                if (intentoActual >= 3)
                {
                    tercerTick.TrySetResult();
                }

                // El primer tick lanza una excepción no controlada — el servicio debe seguir vivo y
                // llegar igual al tercer tick (mitigación R-04, segunda capa).
                if (intentoActual == 1)
                {
                    throw new InvalidOperationException("Fallo simulado del primer ciclo.");
                }

                return Task.CompletedTask;
            });

        var logger = new CapturingLogger<EnvioMailBackgroundService>();
        var servicio = new EnvioMailBackgroundService(
            NuevaScopeFactory(envioMailServiceMock.Object),
            logger,
            IntervaloDeTest);

        await servicio.StartAsync(CancellationToken.None);
        var completado = await Task.WhenAny(tercerTick.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        await servicio.StopAsync(CancellationToken.None);

        Assert.Same(tercerTick.Task, completado);
        Assert.True(contador >= 3);
        Assert.Contains(logger.MensajesCapturados, texto => texto.Contains(nameof(InvalidOperationException), StringComparison.Ordinal));
    }
}
