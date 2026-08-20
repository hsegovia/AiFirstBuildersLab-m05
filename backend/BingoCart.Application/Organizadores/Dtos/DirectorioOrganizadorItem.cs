namespace BingoCart.Application.Organizadores.Dtos;

/// <summary>
/// Un ítem del directorio público de organizadores (spec FEAT-005, Block 1): la organización, el
/// nombre del evento de su bingo activo y la fecha de sorteo. Deliberadamente NO incluye CUIT,
/// mail ni teléfono — <see cref="Infrastructure.Organizadores.DirectorioRepository"/> proyecta
/// estrictamente a estos 3 campos, así que es estructuralmente imposible que un dato de contacto
/// del organizador llegue hasta acá (NFR-02, mitigación R-01 del threat model).
/// </summary>
public sealed record DirectorioOrganizadorItem(string NombreOrganizacion, string NombreEvento, DateTime FechaSorteoUtc);
