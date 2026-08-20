namespace BingoCart.Application.Organizadores.Dtos;

/// <summary>
/// Un ítem del directorio público de organizadores (spec FEAT-005, Block 1; extendido en spec
/// FEAT-008a, Block 1 con <c>Id</c>): el identificador del organizador, la organización, el nombre
/// del evento de su bingo activo y la fecha de sorteo. Deliberadamente NO incluye CUIT, mail ni
/// teléfono — <see cref="Infrastructure.Organizadores.DirectorioRepository"/> proyecta
/// estrictamente a estos 4 campos, así que es estructuralmente imposible que un dato de contacto
/// del organizador llegue hasta acá (NFR-02, mitigación R-01 del threat model). El <c>Id</c> no es
/// un dato sensible (mismo criterio que exponer <c>BingoId</c> en las respuestas de <c>Bingo</c>) —
/// se agrega para que un cliente que lista el directorio pueda pedir después <c>GET
/// /api/cartones/organizador/{organizadorId}</c> (spec FEAT-008a).
/// </summary>
public sealed record DirectorioOrganizadorItem(Guid Id, string NombreOrganizacion, string NombreEvento, DateTime FechaSorteoUtc);
