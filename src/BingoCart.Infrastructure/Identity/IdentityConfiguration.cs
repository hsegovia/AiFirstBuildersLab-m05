using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace BingoCart.Infrastructure.Identity;

/// <summary>
/// Configura la política de contraseña de ASP.NET Core Identity (FR-04, AC-04). Método de
/// extensión sobre <see cref="IdentityBuilder"/> para encadenarse tras
/// `.AddIdentity&lt;...&gt;().AddEntityFrameworkStores&lt;AppDbContext&gt;()` en `Program.cs` — el
/// patrón idiomático de ASP.NET Core Identity para configurar `IdentityOptions` es
/// `services.Configure&lt;IdentityOptions&gt;(...)`, aquí expuesto vía el `IdentityBuilder` para no
/// requerir acceso directo a `IServiceCollection` desde el composition root.
/// </summary>
public static class IdentityConfiguration
{
    public static IdentityBuilder ConfigurarPoliticaDePassword(this IdentityBuilder builder)
    {
        builder.Services.Configure<IdentityOptions>(options =>
        {
            options.Password.RequireDigit = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Password.RequiredLength = 8;
        });

        return builder;
    }
}
