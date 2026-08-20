using System.ComponentModel.DataAnnotations;

namespace BingoCart.Application.Bingos.Dtos;

/// <summary>
/// Request de edición de bingo (spec FEAT-007, Block 2). Mismo patrón que
/// <c>CrearBingoRequest</c>: DataAnnotations en el constructor primario, sin el target
/// <c>property:</c>. Sin <see cref="RangeAttribute"/> en <c>CostoPorCarton</c> — mismo criterio
/// que <c>CrearBingoRequest</c>: la validación de invariantes (fecha futura, costo &gt; 0) vive en
/// <c>Bingo.Actualizar</c> (Domain, Block 1), no acá. <c>[Required]</c> es la única validación de
/// modelo: cubre nulls/ausencia del campo, sin competir con los rangos que valida Domain. Sin
/// <c>CantidadCartones</c> — no es editable (spec FEAT-007, PRD): los cartones ya generados no se
/// tocan.
/// </summary>
public sealed record EditarBingoRequest(
    [Required, MaxLength(200)] string NombreEvento,
    [Required] DateTime FechaSorteoUtc,
    [Required] decimal CostoPorCarton);
