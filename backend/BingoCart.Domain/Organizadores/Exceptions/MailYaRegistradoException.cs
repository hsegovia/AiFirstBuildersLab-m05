using BingoCart.Domain.Common;

namespace BingoCart.Domain.Organizadores.Exceptions;

/// <summary>
/// Se lanza cuando el mail provisto ya pertenece a una cuenta de organizador existente (FR-03).
/// Definida en Domain porque es una invariante del negocio, pero la lanza `OrganizadorService`
/// (Application, Block 3 de FEAT-001a) tras consultar `IIdentityGateway` — Domain no tiene acceso
/// a I/O para verificar la unicidad por sí mismo.
/// </summary>
public sealed class MailYaRegistradoException : DomainException
{
    public MailYaRegistradoException(string message)
        : base(message)
    {
    }
}
