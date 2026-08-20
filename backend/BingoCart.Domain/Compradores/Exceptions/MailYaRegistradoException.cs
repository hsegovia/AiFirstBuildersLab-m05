using BingoCart.Domain.Common;

namespace BingoCart.Domain.Compradores.Exceptions;

/// <summary>
/// Se lanza cuando el mail provisto ya pertenece a una cuenta existente (comprador u organizador,
/// ambos comparten <c>AspNetUsers</c>). Definida en Domain porque es una invariante del negocio, pero
/// la lanza <c>CompradorService</c> (Application, Block 2) tras consultar
/// <c>ICompradorIdentityGateway</c> — Domain no tiene acceso a I/O para verificar la unicidad por sí
/// mismo.
/// </summary>
public sealed class MailYaRegistradoException : DomainException
{
    public MailYaRegistradoException(string message)
        : base(message)
    {
    }
}
