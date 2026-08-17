using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BingoCart.Infrastructure.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BingoCart.Infrastructure.Tests.Auth;

/// <summary>
/// Tests unitarios de <see cref="JwtTokenService"/> (spec FEAT-001b, Block 1). Verifican, con
/// <see cref="JwtSecurityTokenHandler.ValidateToken(string, TokenValidationParameters, out SecurityToken)"/>
/// y los mismos Issuer/Audience/SigningKey usados para emitir, que un token recién emitido es
/// aceptado y que uno con expiración pasada es rechazado sin tolerancia de reloj (NFR-01).
/// </summary>
public class JwtTokenServiceTests
{
    private const string SigningKey = "clave-de-test-para-hmac-sha256-de-al-menos-32-bytes!!";

    private static JwtSettings CrearSettings() => new()
    {
        Issuer = "BingoCart.Tests",
        Audience = "BingoCart.Tests",
        SigningKey = SigningKey,
        ExpirationMinutes = 60
    };

    private static TokenValidationParameters CrearValidationParameters(JwtSettings settings) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = settings.Issuer,
        ValidateAudience = true,
        ValidAudience = settings.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };

    [Fact]
    public void GenerarToken_ConTimeProviderSystem_ProduceUnTokenValidoConLosClaimsEsperados()
    {
        var settings = CrearSettings();
        var service = new JwtTokenService(Options.Create(settings), TimeProvider.System);
        var organizadorId = Guid.NewGuid();
        const string mail = "organizador@example.com";

        var resultado = service.GenerarToken(organizadorId, mail);

        var handler = new JwtSecurityTokenHandler();
        var principal = handler.ValidateToken(resultado.Token, CrearValidationParameters(settings), out _);

        Assert.Equal(organizadorId.ToString(), principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal(mail, principal.FindFirstValue(ClaimTypes.Email));
        Assert.True(resultado.ExpiraEnUtc > DateTime.UtcNow);
    }

    [Fact]
    public void GenerarToken_ConRelojYa61MinutosEnElPasado_ProduceUnTokenQueValidateTokenRechazaPorExpiracion()
    {
        var settings = CrearSettings();
        var relojEnElPasado = new TestTimeProvider(DateTimeOffset.UtcNow.AddMinutes(-61));
        var service = new JwtTokenService(Options.Create(settings), relojEnElPasado);

        var resultado = service.GenerarToken(Guid.NewGuid(), "organizador@example.com");

        var handler = new JwtSecurityTokenHandler();
        Assert.Throws<SecurityTokenExpiredException>(
            () => handler.ValidateToken(resultado.Token, CrearValidationParameters(settings), out _));
    }
}
