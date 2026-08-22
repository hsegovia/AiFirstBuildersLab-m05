using BingoCart.Application.Compras;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BingoCart.Infrastructure.Notificaciones;

/// <summary>
/// <see cref="BackgroundService"/> que dispara <see cref="IEnvioMailService.ProcesarPendientesAsync"/>
/// cada 1 minuto (spec FEAT-009b, Block 3, NFR-01) vía <see cref="PeriodicTimer"/>. Sin precedente
/// de dónde ubicar un <see cref="IHostedService"/> en este proyecto — vive en
/// <c>Infrastructure/Notificaciones/</c> junto al resto de los adaptadores de este bloque
/// (EF Core/MailKit/QuestPDF), no contiene lógica de negocio, solo orquesta el polling.
/// </summary>
/// <remarks>
/// Recibe <see cref="IServiceScopeFactory"/> (no <see cref="IEnvioMailService"/> directo) — hallazgo
/// de CODE al scaffoldear la migración: <c>AddHostedService&lt;T&gt;()</c> registra el
/// <see cref="BackgroundService"/> como Singleton, e <see cref="IEnvioMailService"/> es Scoped
/// (depende, transitivamente, de <c>AppDbContext</c>, también Scoped) — un Singleton no puede
/// consumir un Scoped directo (excepción de validación de DI). Crea un <see cref="IServiceScope"/>
/// nuevo por tick, mismo patrón documentado por Microsoft para <c>BackgroundService</c> con
/// dependencias Scoped.
/// </remarks>
public sealed class EnvioMailBackgroundService : BackgroundService
{
    private static readonly TimeSpan IntervaloPorDefecto = TimeSpan.FromMinutes(1); // NFR-01

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EnvioMailBackgroundService> _logger;
    private readonly TimeSpan _intervalo;

    /// <summary>
    /// <paramref name="intervalo"/> es opcional (por defecto 1 minuto, NFR-01): no hay ningún
    /// registro en DI para <see cref="TimeSpan"/>, así que <c>AddHostedService&lt;T&gt;()</c>
    /// activa este constructor con el valor por defecto en producción. Los tests inyectan un
    /// intervalo corto explícito, sin depender del minuto real.
    /// </summary>
    public EnvioMailBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<EnvioMailBackgroundService> logger,
        TimeSpan? intervalo = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _intervalo = intervalo ?? IntervaloPorDefecto;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_intervalo);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var envioMailService = scope.ServiceProvider.GetRequiredService<IEnvioMailService>();
                await envioMailService.ProcesarPendientesAsync();
            }
            catch (Exception ex)
            {
                // Segunda capa de la mitigación R-04 (threat model) — la primera, por-envío, ya
                // vive en EnvioMailService (Application, Block 2). Si ProcesarPendientesAsync
                // lanzara algo no controlado, este catch evita que el BackgroundService muera
                // permanentemente: loguea (R-01/R-02: únicamente el tipo de excepción, nunca
                // ex.Message ni PII) y sigue vivo para el próximo tick.
                _logger.LogWarning(
                    "Fallo un ciclo de procesamiento de EnviosMail. Tipo de excepcion: {TipoExcepcion}.",
                    ex.GetType().Name);
            }
        }
    }
}
