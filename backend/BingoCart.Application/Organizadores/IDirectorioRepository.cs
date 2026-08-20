namespace BingoCart.Application.Organizadores;

/// <summary>
/// Puerto de lectura del directorio público de organizadores (spec FEAT-005, Block 1). Distinto de
/// <see cref="IIdentityGateway"/> (puerto de credenciales) y de
/// <see cref="Bingos.IBingoRepository"/> (puerto del agregado <c>Bingo</c>): esta consulta cruza
/// <c>Bingos</c> con <c>Users</c> (Identity) para un reporte público de solo lectura — no es un
/// concern de ninguno de los dos agregados existentes (decisión de PLAN, ver spec). Application
/// solo conoce esta firma — el detalle de EF Core y el cruce con Identity viven en
/// <c>BingoCart.Infrastructure.Organizadores.DirectorioRepository</c>.
/// </summary>
public interface IDirectorioRepository
{
    /// <summary>
    /// Devuelve la página <paramref name="page"/> (1-based) de tamaño <paramref name="pageSize"/>
    /// de los organizadores con al menos un bingo cuya <c>FechaSorteoUtc</c> es posterior a
    /// <paramref name="ahoraUtc"/>, ordenados por <c>FechaSorteoUtc</c> ascendente, junto con el
    /// total real (sin paginar). <paramref name="page"/>/<paramref name="pageSize"/> se asumen ya
    /// validados/clampeados por el llamador (Application, Block 2) — este puerto no revalida.
    /// </summary>
    Task<DirectorioPaginado> ListarActivosAsync(DateTime ahoraUtc, int page, int pageSize);
}
