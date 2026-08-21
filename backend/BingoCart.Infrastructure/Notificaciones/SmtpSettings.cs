namespace BingoCart.Infrastructure.Notificaciones;

/// <summary>
/// Config bindeada desde la sección <c>Smtp</c> de <c>appsettings.json</c>/
/// <c>appsettings.Development.json</c> (spec FEAT-009b, Block 3) — mismo patrón de secreto de
/// desarrollo sobreescribible por variable de entorno ya usado para <c>Tde:MasterKeyPassword</c>/
/// <c>Jwt:SigningKey</c>.
/// </summary>
public sealed record SmtpSettings
{
    public string Host { get; init; } = string.Empty;

    public int Port { get; init; }

    public string User { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string From { get; init; } = string.Empty;

    /// <summary>
    /// Único campo de <see cref="SmtpSettings"/> sin precedente textual en el spec (ver reporte del
    /// bloque, sección "Assumptions"): <c>false</c> por defecto (ausente en <c>appsettings.json</c>
    /// de Producción — nunca se fija ahí). En <c>appsettings.Development.json</c> se fija en
    /// <c>true</c> únicamente porque <c>smtp4dev</c> (contenedor dev/test-only, R-08) usa un
    /// certificado autofirmado sin CA de confianza — sin este flag, la conexión StartTls
    /// obligatoria (R-03) fallaría siempre contra ese contenedor por validación de certificado, un
    /// problema distinto del que R-03 mitiga (texto plano). Nunca debe ser <c>true</c> contra un
    /// SMTP real.
    /// </summary>
    public bool AllowInvalidCertificate { get; init; }
}
