namespace BingoCart.Application.Organizadores;

/// <summary>
/// Resultado de intentar autenticar un organizador (spec FEAT-001b, Block 2). Distingue los tres
/// desenlaces posibles de <see cref="IIdentityGateway.AutenticarAsync"/> sin que Application
/// dependa de ningún tipo de Identity.
/// </summary>
public enum EstadoAutenticacion
{
    Exitoso,
    CredencialesInvalidas,
    CuentaBloqueada,
}

/// <summary>
/// <c>OrganizadorId</c> va poblado solo cuando <c>Estado == EstadoAutenticacion.Exitoso</c>, con el
/// <see cref="Guid"/> del usuario de Identity ya recuperado por mail — por diseño de FEAT-001a es
/// el mismo id que <c>Organizador.Id</c>, sin ninguna búsqueda adicional que hacer.
/// </summary>
public sealed record ResultadoAutenticacion(EstadoAutenticacion Estado, Guid? OrganizadorId);
