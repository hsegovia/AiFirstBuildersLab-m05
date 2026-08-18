namespace BingoCart.Application.Bingos;

/// <summary>
/// Puerto de generación de conjuntos de números de cartón (spec FEAT-003, Block 2). Application
/// solo conoce esta firma — el detalle criptográfico (CSPRNG) vive en
/// <c>BingoCart.Infrastructure.Bingos.CartonNumberGenerator</c>.
/// </summary>
public interface ICartonNumberGenerator
{
    /// <summary>
    /// Genera <paramref name="cantidad"/> conjuntos de 10 números (1-90) cada uno, garantizados
    /// distintos ENTRE SÍ (comparando el conjunto completo, no elemento por elemento). No valida
    /// nada respecto a <c>Carton</c> — esa validación la hace el dominio (Block 1) cuando
    /// Application (Block 4) arme los <c>Carton</c> con estos conjuntos.
    /// </summary>
    IReadOnlyList<IReadOnlyList<int>> GenerarConjuntosUnicos(int cantidad);
}
