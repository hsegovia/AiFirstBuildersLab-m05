using BingoCart.Domain.Common;

namespace BingoCart.Domain.Carritos.Exceptions;

/// <summary>
/// Se lanza cuando <c>cartonIdsDescartados</c> supera el límite máximo de 50 elementos por request
/// (corrección post-SAST, F-SAST-14 sobre FEAT-008b): sin este límite, un cliente sin autenticar
/// podía mandar un array de cientos de miles de <c>Guid</c> en
/// <c>POST /api/carrito/tandas/nueva</c>, agrandando sin control el <c>NOT IN (...)</c> que arma
/// <c>DescubrimientoRepository.ConstruirClausulaExclusion</c> — agotamiento de recursos, no
/// inyección (el tipo <c>Guid</c> ya garantiza eso). Vive en <c>Domain.Carritos.Exceptions</c>,
/// mismo criterio que <c>CartonInexistenteException</c>/<c>CartonYaReservadoException</c>: el
/// carrito es lo que no puede completar la operación.
/// </summary>
public sealed class CantidadDescartadosExcedeLimiteException : DomainException
{
    public CantidadDescartadosExcedeLimiteException(string message)
        : base(message)
    {
    }
}
