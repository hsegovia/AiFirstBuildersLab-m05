using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BingoCart.Application.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BingoCart.Infrastructure.Auth;

/// <summary>
/// Única clase del sistema que conoce el algoritmo de firma y la clave usados para emitir JWT de
/// organizador (spec FEAT-001b, Block 1). Recibe <see cref="TimeProvider"/> por constructor (en vez
/// de un overload interno/<c>InternalsVisibleTo</c>, sin precedente en este repo) para que los
/// tests puedan construir un servicio cuyo reloj ya está en el pasado y así emitir tokens
/// "ya vencidos" sin esperar los 60 minutos reales de expiración.
/// </summary>
public sealed class JwtTokenService : IJwtTokenService
{
    private readonly JwtSettings _settings;
    private readonly TimeProvider _timeProvider;

    public JwtTokenService(IOptions<JwtSettings> settings, TimeProvider timeProvider)
    {
        _settings = settings.Value;
        _timeProvider = timeProvider;
    }

    public TokenGenerado GenerarToken(Guid organizadorId, string mail)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, organizadorId.ToString()),
            new Claim(ClaimTypes.Email, mail)
        };

        var signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var ahora = _timeProvider.GetUtcNow().UtcDateTime;
        var expiracion = ahora + TimeSpan.FromMinutes(_settings.ExpirationMinutes);

        // NotBefore se fija explícitamente con el mismo reloj que Expires (en vez de dejar que
        // JwtSecurityToken use DateTime.UtcNow real por defecto): con un TestTimeProvider ya en el
        // pasado, un NotBefore "real" quedaría por delante de un Expires "falso", y
        // Validators.ValidateLifetime lanzaría SecurityTokenInvalidLifetimeException en vez del
        // SecurityTokenExpiredException que exige el test de expiración de este bloque.
        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            notBefore: ahora,
            expires: expiracion,
            signingCredentials: signingCredentials);

        var jwt = new JwtSecurityTokenHandler().WriteToken(token);
        return new TokenGenerado(jwt, expiracion);
    }
}
