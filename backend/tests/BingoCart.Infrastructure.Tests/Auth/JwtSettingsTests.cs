using System.Text;
using BingoCart.Infrastructure.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BingoCart.Infrastructure.Tests.Auth;

/// <summary>
/// Replica en aislamiento (sin levantar un host completo) el mismo registro de
/// <see cref="JwtSettings"/> que <c>Program.cs</c> hace vía
/// <c>AddOptions&lt;JwtSettings&gt;().Bind(config).Validate(...)</c> (spec FEAT-001b, Block 1), para
/// verificar el error handling documentado: el proceso no debe poder resolver
/// <see cref="IOptions{TOptions}"/> de <see cref="JwtSettings"/> ni con la clave ausente ni con una
/// clave débil (&lt;32 bytes). Hallazgo respecto al spec: <c>required</c> en el record es solo una
/// verificación de compilador — <c>AddOptions&lt;T&gt;().Bind(...)</c> construye la instancia vía
/// reflection (bypassea el required) y deja <c>SigningKey</c> en <c>null</c> si falta en
/// configuración, por lo que el predicado de <c>Validate</c> debe chequear null/vacío
/// explícitamente (ver Program.cs) para fallar siempre con <see cref="OptionsValidationException"/>
/// en vez de un <see cref="ArgumentNullException"/> sin contexto.
/// </summary>
public class JwtSettingsTests
{
    private static ServiceProvider ConstruirServiceProvider(IDictionary<string, string?> valores)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(valores)
            .Build();

        var services = new ServiceCollection();
        services
            .AddOptions<JwtSettings>()
            .Bind(configuration.GetSection("Jwt"))
            .Validate(
                settings => !string.IsNullOrWhiteSpace(settings.SigningKey)
                    && Encoding.UTF8.GetByteCount(settings.SigningKey) >= 32,
                "Jwt:SigningKey debe tener al menos 32 bytes");

        return services.BuildServiceProvider();
    }

    [Fact]
    public void IOptionsJwtSettings_SinSigningKeyEnConfiguracion_FallaLaValidacionDeOpciones()
    {
        using var provider = ConstruirServiceProvider(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "BingoCart",
            ["Jwt:Audience"] = "BingoCart",
        });

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<JwtSettings>>().Value);
    }

    [Fact]
    public void IOptionsJwtSettings_ConSigningKeyDeMenosDe32Bytes_FallaLaValidacionDeOpciones()
    {
        using var provider = ConstruirServiceProvider(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "BingoCart",
            ["Jwt:Audience"] = "BingoCart",
            ["Jwt:SigningKey"] = "clave-corta",
        });

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<JwtSettings>>().Value);

        Assert.Contains("Jwt:SigningKey debe tener al menos 32 bytes", exception.Message);
    }
}
