using System.ComponentModel.DataAnnotations;

namespace BingoCart.Application.Bingos.Dtos;

/// <summary>
/// Request de creación de bingo (FR-01, FR-02, FR-04, FR-07). Mismo patrón que
/// <c>RegistrarOrganizadorRequest</c>: DataAnnotations en el constructor primario, sin el target
/// <c>property:</c> (ASP.NET Core exige la metadata en el parámetro del constructor primario para
/// poder bindear <c>[FromBody]</c> correctamente en un record posicional).
/// <b>Sin <see cref="RangeAttribute"/></b> en <c>CantidadCartones</c> ni <c>CostoPorCarton</c> —
/// decisión cerrada en PLAN (ver spec FEAT-003, Block 4): con <c>[ApiController]</c>, un
/// <c>Range</c> ahí dispararía el <c>InvalidModelStateResponseFactory</c> (400
/// <c>DatosInvalidos</c>) ANTES de que <c>Bingo.Crear</c> se ejecute siquiera, haciendo
/// inalcanzables <c>CantidadCartonesExcedeLimiteException</c>/
/// <c>CantidadCartonesInvalidaException</c>/<c>CostoPorCartonInvalidoException</c> (Block 1) —
/// mismo criterio que ya sigue <c>RegistrarOrganizadorRequest</c> con Cuit/Telefono/Password.
/// <c>[Required]</c> es la única validación de modelo: cubre nulls/ausencia del campo, sin competir
/// con los rangos que valida Domain.
/// </summary>
public sealed record CrearBingoRequest(
    [Required, MaxLength(200)] string NombreEvento,
    [Required] DateTime FechaSorteoUtc,
    [Required] int CantidadCartones,
    [Required] decimal CostoPorCarton);
