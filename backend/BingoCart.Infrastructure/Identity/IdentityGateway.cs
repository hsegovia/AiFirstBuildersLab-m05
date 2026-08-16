using BingoCart.Application.Organizadores;
using BingoCart.Domain.Organizadores;
using Microsoft.AspNetCore.Identity;

namespace BingoCart.Infrastructure.Identity;

/// <summary>
/// Única clase de Infrastructure que conoce <see cref="UserManager{TUser}"/>. Implementa el puerto
/// <see cref="IIdentityGateway"/> (Application, Block 3) para que `OrganizadorService` nunca
/// dependa del tipo concreto de Identity.
/// </summary>
public sealed class IdentityGateway : IIdentityGateway
{
    private readonly UserManager<ApplicationUser> _userManager;

    public IdentityGateway(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<bool> ExisteMailAsync(string mail)
    {
        var usuarioExistente = await _userManager.FindByEmailAsync(mail);
        return usuarioExistente is not null;
    }

    public async Task<IdentityGatewayResult> CrearUsuarioAsync(Organizador organizador, string password)
    {
        var usuario = new ApplicationUser
        {
            // Mismo Id que la entidad de Domain: mantiene Organizador.Id y ApplicationUser.Id en
            // sincronía en vez de dejar que Identity genere uno nuevo (Guid.Empty por defecto en
            // IdentityUser<Guid>()). EF Core respeta un valor de PK no-default al insertar.
            Id = organizador.Id,
            UserName = organizador.Mail,
            Email = organizador.Mail,
            // Activación inmediata sin verificación de mail (FR-06).
            EmailConfirmed = true,
            NombreOrganizacion = organizador.NombreOrganizacion,
            Cuit = organizador.Cuit,
            Telefono = organizador.Telefono,
        };

        var resultado = await _userManager.CreateAsync(usuario, password);

        var errores = resultado.Errors.Select(error => error.Description).ToList();
        return new IdentityGatewayResult(resultado.Succeeded, errores);
    }
}
