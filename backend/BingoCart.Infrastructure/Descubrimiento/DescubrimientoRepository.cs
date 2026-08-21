using BingoCart.Application.Descubrimiento;
using BingoCart.Application.Descubrimiento.Dtos;
using BingoCart.Domain.Bingos;
using BingoCart.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BingoCart.Infrastructure.Descubrimiento;

/// <summary>
/// Implementa <see cref="IDescubrimientoRepository"/> (Application, Block 1 del spec FEAT-008a)
/// contra <see cref="AppDbContext"/> — capa de infraestructura pura, sin decisiones de negocio (qué
/// es "activo" ya viene resuelto por el filtro SQL explícito; cuántos cartones pedir lo decide
/// Application en Block 2). Primer punto del proyecto que hace selección aleatoria a nivel de base
/// de datos: <c>ObtenerAleatoriosGlobalAsync</c>/<c>ObtenerAleatoriosDeBingoAsync</c> usan SQL crudo
/// con <c>ORDER BY NEWID()</c> (SQL Server) — nunca <c>.OrderBy(_ => Guid.NewGuid())</c> (no
/// traducible a SQL por EF Core) (decisión de PLAN, ver spec). Desde FEAT-008b (Block 2), la
/// cláusula opcional <c>NOT IN (...)</c> de exclusión no puede armarse con <c>FromSqlInterpolated</c>
/// (cada hueco se parametriza como UN único valor, no como una lista) — usan <c>FromSqlRaw</c> con
/// los parámetros propios (<c>cantidad</c>/<c>ahoraUtc</c>/<c>bingoId</c>) vía sus placeholders
/// <c>{0}</c>/<c>{1}</c>, concatenando (no interpolando con <c>$"..."</c>, para no disparar el
/// analizador EF1002) el texto ya validado de la cláusula de exclusión. <c>ObtenerResumenBingosAsync</c>
/// usa LINQ normal con un JOIN a <c>Users</c>, mismo patrón que <c>DirectorioRepository</c> (FEAT-005).
/// </summary>
public sealed class DescubrimientoRepository : IDescubrimientoRepository
{
    private readonly AppDbContext _context;

    public DescubrimientoRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<Carton>> ObtenerAleatoriosGlobalAsync(
        DateTime ahoraUtc, int cantidad, IReadOnlyCollection<Guid> excluirCartonIds)
    {
        // La cláusula de exclusión (spec FEAT-008b, Block 2) no puede armarse como un hueco más de
        // FromSqlInterpolated: cada `{}` de FromSqlInterpolated se parametriza como UN único valor,
        // así que un `string.Join(",", excluirCartonIds)` interpolado ahí terminaría comparando
        // `c.Id` (uniqueidentifier) contra un solo parámetro string con comas — SQL Server no puede
        // convertir eso a GUID. Por eso se arma el texto de la cláusula en C# (solo con `Guid.
        // ToString()`, que nunca produce comillas ni metacaracteres SQL — sin riesgo de inyección) y
        // se concatena directo al SQL, mientras `cantidad`/`ahoraUtc` siguen yendo parametrizados
        // vía los placeholders `{0}`/`{1}` de FromSqlRaw.
        var clausulaExclusion = ConstruirClausulaExclusion(excluirCartonIds);

        // Concatenación (no interpolación de C#) a propósito: `FromSqlRaw` con un string
        // interpolado dispara el analizador EF1002 (no distingue un hueco seguro de uno inseguro).
        // `{0}`/`{1}` siguen siendo los placeholders propios de `FromSqlRaw`, parametrizados por EF
        // Core — `clausulaExclusion` es texto ya validado (solo GUIDs), no una interpolación.
        // Subquery NOT EXISTS nueva (spec FEAT-009a, Block 1, FR-07): texto SQL COMPLETAMENTE FIJO,
        // sin ningún valor interpolado ni concatenado — a diferencia de `clausulaExclusion` (arriba),
        // que sí concatena GUIDs de `excluirCartonIds`. Un cartón con una fila en `CompraCartones`
        // nunca vuelve a aparecer en descubrimiento global (threat model FEAT-009a, R-05: evaluado
        // como más seguro que la cláusula NOT IN existente, no hay ninguna superficie de inyección
        // que auditar porque no hay ningún dato variable en este string).
        var sql = "SELECT TOP ({0}) c.* " +
                   "FROM Cartones c " +
                   "INNER JOIN Bingos b ON b.Id = c.BingoId " +
                   "WHERE b.FechaSorteoUtc > {1} " +
                   "AND NOT EXISTS (SELECT 1 FROM CompraCartones cc WHERE cc.CartonId = c.Id) " +
                   clausulaExclusion + " " +
                   "ORDER BY NEWID()";

        return await _context.Cartones
            .FromSqlRaw(sql, cantidad, ahoraUtc)
            .AsNoTracking()
            .ToListAsync();
    }

    public Task<bool> ExisteOrganizadorAsync(Guid organizadorId)
    {
        return _context.Users.AnyAsync(u => u.Id == organizadorId);
    }

    public Task<Guid?> ObtenerBingoActivoDeOrganizadorAsync(Guid organizadorId, DateTime ahoraUtc)
    {
        return _context.Bingos
            .Where(b => b.OrganizadorId == organizadorId && b.FechaSorteoUtc > ahoraUtc)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefaultAsync();
    }

    public async Task<IReadOnlyList<Carton>> ObtenerAleatoriosDeBingoAsync(
        Guid bingoId, int cantidad, IReadOnlyCollection<Guid> excluirCartonIds)
    {
        var clausulaExclusion = ConstruirClausulaExclusion(excluirCartonIds);

        // Misma subquery NOT EXISTS fija que ObtenerAleatoriosGlobalAsync (arriba) — ver ese
        // comentario para el detalle de por qué no es una superficie de inyección.
        var sql = "SELECT TOP ({0}) c.* " +
                   "FROM Cartones c " +
                   "WHERE c.BingoId = {1} " +
                   "AND NOT EXISTS (SELECT 1 FROM CompraCartones cc WHERE cc.CartonId = c.Id) " +
                   clausulaExclusion + " " +
                   "ORDER BY NEWID()";

        return await _context.Cartones
            .FromSqlRaw(sql, cantidad, bingoId)
            .AsNoTracking()
            .ToListAsync();
    }

    // Vacía → sin cláusula (evita un `NOT IN ()` inválido en SQL Server, decisión de PLAN). No
    // vacía → GUIDs formateados como literales entre comillas simples; seguro porque `Guid.
    // ToString()` (formato "D" por defecto) solo produce dígitos hexadecimales y guiones.
    private static string ConstruirClausulaExclusion(IReadOnlyCollection<Guid> excluirCartonIds)
    {
        if (excluirCartonIds.Count == 0)
        {
            return string.Empty;
        }

        var idsFormateados = string.Join(",", excluirCartonIds.Select(id => $"'{id}'"));
        return $"AND c.Id NOT IN ({idsFormateados})";
    }

    public async Task<IReadOnlyList<BingoResumen>> ObtenerResumenBingosAsync(IReadOnlyCollection<Guid> bingoIds)
    {
        return await _context.Bingos
            .Join(_context.Users, b => b.OrganizadorId, u => u.Id, (b, u) => new { b, u })
            .Where(x => bingoIds.Contains(x.b.Id))
            // `NombreOrganizacion` es nullable en el esquema desde FEAT-009a (el comprador no lo
            // completa), pero acá `u` siempre proviene de `Bingo.OrganizadorId` — un organizador,
            // que sí lo completa siempre — el `!` es seguro, no oculta un caso real de nulidad.
            .Select(x => new BingoResumen(x.b.Id, x.u.NombreOrganizacion!, x.b.NombreEvento, x.b.CostoPorCarton, x.b.FechaSorteoUtc))
            .ToListAsync();
    }
}
