using BingoCart.Application.Descubrimiento.Dtos;
using BingoCart.Domain.Bingos;

namespace BingoCart.Application.Descubrimiento;

/// <summary>
/// Puerto de lectura para el descubrimiento público de cartones (spec FEAT-008a, Block 1) — nuevo,
/// no agregado a <see cref="Bingos.IBingoRepository"/> ni a <c>Organizadores.IDirectorioRepository</c>:
/// cruza <c>Bingos</c>+<c>Cartones</c>+<c>Users</c> con una lógica de selección propia (aleatoriedad),
/// categóricamente distinta de ambos puertos existentes (decisión de PLAN, ver spec). Application
/// solo conoce esta firma — el detalle de EF Core, incluida la selección aleatoria vía SQL crudo
/// (<c>ORDER BY NEWID()</c>), vive en <c>BingoCart.Infrastructure.Descubrimiento.DescubrimientoRepository</c>.
/// </summary>
public interface IDescubrimientoRepository
{
    /// <summary>
    /// Devuelve hasta <paramref name="cantidad"/> cartones elegidos al azar entre todos los bingos
    /// con <c>FechaSorteoUtc</c> posterior a <paramref name="ahoraUtc"/> (Método 1 — FR-01). Menos
    /// de <paramref name="cantidad"/> cartones elegibles no es un error: devuelve todos los
    /// disponibles.
    /// </summary>
    Task<IReadOnlyList<Carton>> ObtenerAleatoriosGlobalAsync(DateTime ahoraUtc, int cantidad);

    /// <summary>
    /// Indica si existe un organizador con <paramref name="organizadorId"/> (FR-05/AC-03) —
    /// consulta separada de <see cref="ObtenerBingoActivoDeOrganizadorAsync"/> para poder distinguir
    /// "organizador inexistente" (404) de "organizador sin bingo activo" (200 vacío).
    /// </summary>
    Task<bool> ExisteOrganizadorAsync(Guid organizadorId);

    /// <summary>
    /// Devuelve el <c>Id</c> del bingo con <c>FechaSorteoUtc</c> posterior a
    /// <paramref name="ahoraUtc"/> de <paramref name="organizadorId"/>, o <c>null</c> si no tiene
    /// ninguno vigente (FR-05/AC-04/AC-07).
    /// </summary>
    Task<Guid?> ObtenerBingoActivoDeOrganizadorAsync(Guid organizadorId, DateTime ahoraUtc);

    /// <summary>
    /// Devuelve hasta <paramref name="cantidad"/> cartones elegidos al azar del bingo
    /// <paramref name="bingoId"/> (Método 2 — FR-05). Menos de <paramref name="cantidad"/> cartones
    /// no es un error: devuelve todos los disponibles.
    /// </summary>
    Task<IReadOnlyList<Carton>> ObtenerAleatoriosDeBingoAsync(Guid bingoId, int cantidad);

    /// <summary>
    /// Devuelve el <see cref="BingoResumen"/> de cada <c>Guid</c> en <paramref name="bingoIds"/> —
    /// consulta LINQ normal (sin SQL crudo), no filtra por "activo" ni pagina: los ids ya vienen
    /// filtrados por el llamador.
    /// </summary>
    Task<IReadOnlyList<BingoResumen>> ObtenerResumenBingosAsync(IReadOnlyCollection<Guid> bingoIds);
}
