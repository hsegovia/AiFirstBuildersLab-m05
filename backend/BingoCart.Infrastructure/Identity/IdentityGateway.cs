using BingoCart.Application.Compradores;
using BingoCart.Application.Organizadores;
using BingoCart.Domain.Compradores;
using BingoCart.Domain.Organizadores;
using Microsoft.AspNetCore.Identity;

namespace BingoCart.Infrastructure.Identity;

/// <summary>
/// Única clase de Infrastructure que conoce <see cref="UserManager{TUser}"/>. Implementa DOS
/// puertos sobre la misma instancia — <see cref="IIdentityGateway"/> (Application, organizador) y
/// <see cref="ICompradorIdentityGateway"/> (Application, comprador, spec FEAT-009a) — decisión de
/// PLAN: ambos envuelven el mismo <c>UserManager&lt;ApplicationUser&gt;</c>/
/// <c>SignInManager&lt;ApplicationUser&gt;</c>, evitando duplicar el wiring de Identity en dos clases
/// de infraestructura casi idénticas.
/// </summary>
public sealed class IdentityGateway : IIdentityGateway, ICompradorIdentityGateway
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;

    public IdentityGateway(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
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

    public async Task<ResultadoAutenticacion> AutenticarAsync(string mail, string password)
    {
        var usuario = await _userManager.FindByEmailAsync(mail);
        if (usuario is null)
        {
            // Mail inexistente: no hay cuenta cuyo contador de intentos fallidos incrementar.
            return new ResultadoAutenticacion(EstadoAutenticacion.CredencialesInvalidas, null);
        }

        var resultado = await _signInManager.CheckPasswordSignInAsync(usuario, password, lockoutOnFailure: true);

        if (resultado.Succeeded)
        {
            return new ResultadoAutenticacion(EstadoAutenticacion.Exitoso, usuario.Id);
        }

        if (resultado.IsLockedOut)
        {
            return new ResultadoAutenticacion(EstadoAutenticacion.CuentaBloqueada, null);
        }

        // Cualquier otro resultado (password incorrecta, IsNotAllowed, etc.) se trata como
        // credenciales inválidas, sin distinguir causa (AC-02).
        return new ResultadoAutenticacion(EstadoAutenticacion.CredencialesInvalidas, null);
    }

    // Implementación de ICompradorIdentityGateway (spec FEAT-009a, Block 1) — mismo UserManager que
    // arriba, sin ningún estado ni wiring adicional.

    public async Task<IdentityGatewayResult> CrearUsuarioAsync(Comprador comprador, string password)
    {
        var usuario = new ApplicationUser
        {
            // Mismo Id que la entidad de Domain, mismo criterio que el organizador.
            Id = comprador.Id,
            UserName = comprador.Mail,
            Email = comprador.Mail,
            EmailConfirmed = true,
            Apellido = comprador.Apellido,
            Nombre = comprador.Nombre,
            Cuit = comprador.Cuit,
        };

        var resultado = await _userManager.CreateAsync(usuario, password);

        if (!resultado.Succeeded)
        {
            var erroresCreacion = resultado.Errors.Select(error => error.Description).ToList();
            return new IdentityGatewayResult(false, erroresCreacion);
        }

        // Primer uso real de AspNetRoles/AspNetUserRoles del proyecto (decisión de PLAN, spec
        // FEAT-009a) — el rol "Comprador" ya fue sembrado idempotentemente al arrancar (Program.cs,
        // Block 3).
        var resultadoRol = await _userManager.AddToRoleAsync(usuario, "Comprador");

        var errores = resultadoRol.Errors.Select(error => error.Description).ToList();
        return new IdentityGatewayResult(resultadoRol.Succeeded, errores);
    }
}
