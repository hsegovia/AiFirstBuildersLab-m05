namespace BingoCart.Infrastructure.Tests.Auth;

/// <summary>
/// Doble de test de <see cref="TimeProvider"/>: sobreescribe únicamente <see cref="GetUtcNow"/>
/// para devolver una fecha fija/pasada, permitiendo construir un <c>JwtTokenService</c> cuyo reloj
/// ya está en el pasado (spec FEAT-001b, Block 1) y así emitir un token "ya vencido" sin esperar
/// minutos reales en el test. Definido en el proyecto de test, sin ningún paquete NuGet adicional.
/// </summary>
public sealed class TestTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _fixedUtcNow;

    public TestTimeProvider(DateTimeOffset fixedUtcNow)
    {
        _fixedUtcNow = fixedUtcNow;
    }

    public override DateTimeOffset GetUtcNow() => _fixedUtcNow;
}
