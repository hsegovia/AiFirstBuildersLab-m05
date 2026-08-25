using BingoCart.Application.Compradores;
using BingoCart.Application.Compradores.Dtos;
using BingoCart.Application.Organizadores;
using BingoCart.Domain.Compradores;
using BingoCart.Domain.Organizadores;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

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

    // Datos de cuenta (spec FEAT-009d, Block 5) — actualización y lectura, mismo UserManager que
    // arriba, sin ningún estado ni wiring adicional.

    public async Task<CuentaCompradorResponse?> ObtenerCuentaAsync(Guid compradorId)
    {
        var usuario = await _userManager.FindByIdAsync(compradorId.ToString());
        if (usuario is null)
        {
            return null;
        }

        return new CuentaCompradorResponse(
            usuario.Apellido ?? string.Empty,
            usuario.Nombre ?? string.Empty,
            usuario.Cuit,
            usuario.Email ?? string.Empty);
    }

    // Reutiliza CheckPasswordAsync — el mismo primitivo que SignInManager.CheckPasswordSignInAsync
    // (usado por AutenticarAsync arriba) delega internamente para comparar el hash — sin leer ni
    // comparar PasswordHash a mano en ningún punto. A diferencia de AutenticarAsync, esto NO pasa
    // por CheckPasswordSignInAsync: esta operación no es un intento de login (la sesión ya es
    // válida, el JWT ya autenticó al comprador), es la re-verificación de identidad que exige
    // ActualizarCuentaAsync (FR-13); mezclarla con el contador de lockout de inicio de sesión
    // penalizaría al comprador por errores de tipeo en un formulario de datos, no en un login.
    public async Task<bool> VerificarPasswordAsync(Guid compradorId, string password)
    {
        var usuario = await _userManager.FindByIdAsync(compradorId.ToString());
        if (usuario is null)
        {
            return false;
        }

        return await _userManager.CheckPasswordAsync(usuario, password);
    }

    public async Task<bool> ExisteMailDeOtraCuentaAsync(Guid compradorId, string mail)
    {
        var usuario = await _userManager.FindByEmailAsync(mail);
        return usuario is not null && usuario.Id != compradorId;
    }

    public async Task<bool> ExisteCuitDeOtraCuentaAsync(Guid compradorId, string cuit)
    {
        var usuario = await _userManager.Users.FirstOrDefaultAsync(u => u.Cuit == cuit);
        return usuario is not null && usuario.Id != compradorId;
    }

    // La parte que rompe si se hace ingenuamente (R-03): UserManager.SetEmailAsync toca Email/
    // NormalizedEmail pero NUNCA UserName/NormalizedUserName. El alta (CrearUsuarioAsync, arriba)
    // setea UserName = mail, así que si esta actualización solo llamara a SetEmailAsync,
    // NormalizedUserName quedaría desincronizado del mail real — y AspNetUsers tiene un índice
    // único sobre esa columna. Se mutan los cinco campos (Email/NormalizedEmail/UserName/
    // NormalizedUserName/SecurityStamp) EXPLÍCITAMENTE y se persisten en una única llamada a
    // UpdateAsync, en vez de encadenar SetEmailAsync + SetUserNameAsync (cada una hace su propio
    // round-trip Y SetEmailAsync además resetea EmailConfirmed a false como efecto colateral no
    // documentado en el spec, deshaciendo la activación inmediata sin verificación de mail del
    // alta) — normalizando con los mismos métodos que usaría Identity (NormalizeEmail/NormalizeName)
    // para no duplicar a mano la lógica de normalización configurada.
    public async Task<IdentityGatewayResult> ActualizarDatosAsync(
        Guid compradorId, string apellido, string nombre, string cuit, string mail)
    {
        var usuario = await _userManager.FindByIdAsync(compradorId.ToString());
        if (usuario is null)
        {
            return new IdentityGatewayResult(false, new List<string> { "No se encontró la cuenta del comprador." });
        }

        usuario.Apellido = apellido;
        usuario.Nombre = nombre;
        usuario.Cuit = cuit;
        usuario.Email = mail;
        usuario.NormalizedEmail = _userManager.NormalizeEmail(mail);
        usuario.UserName = mail;
        usuario.NormalizedUserName = _userManager.NormalizeName(mail);
        usuario.SecurityStamp = Guid.NewGuid().ToString();

        var resultado = await _userManager.UpdateAsync(usuario);

        var errores = resultado.Errors.Select(error => error.Description).ToList();
        return new IdentityGatewayResult(resultado.Succeeded, errores);
    }
}
