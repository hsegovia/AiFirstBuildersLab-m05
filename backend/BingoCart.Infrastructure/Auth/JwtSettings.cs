namespace BingoCart.Infrastructure.Auth;

/// <summary>
/// Opciones de configuración para la emisión/validación de JWT (sección "Jwt" de
/// appsettings). <c>SigningKey</c> es <c>required</c> a propósito: si falta en configuración, el
/// binding de <see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/> lanza al arrancar en
/// vez de dejar el proceso emitiendo tokens sin firma válida (spec FEAT-001b, Block 1, Error
/// handling).
/// </summary>
public sealed record JwtSettings
{
    public required string Issuer { get; init; }

    public required string Audience { get; init; }

    public required string SigningKey { get; init; }

    public int ExpirationMinutes { get; init; } = 60;
}
