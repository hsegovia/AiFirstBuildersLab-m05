using System.ComponentModel.DataAnnotations;

namespace BingoCart.Application.Bingos.Dtos;

/// <summary>
/// Query de listado paginado de bingos propios (FR-02, FR-05 de <c>prd-FEAT-004.md</c>). Record
/// posicional, mismo patrón sintáctico que <see cref="CrearBingoRequest"/> (DataAnnotations en el
/// constructor primario), pero a diferencia de aquel SÍ usa <see cref="RangeAttribute"/>: acá no
/// hay ninguna invariante de <c>Bingo</c> que proteger — la paginación no es una regla de negocio
/// del agregado — así que el 400 automático de <c>InvalidModelStateResponseFactory</c> (ya
/// registrado en <c>Program.cs</c>) es exactamente lo que pide AC-07 del PRD, sin duplicar ese
/// mecanismo a mano (decisión cerrada en PLAN, ver spec FEAT-004). <c>PageSize</c> &gt; 100 NO se
/// rechaza acá — se clampea en <c>BingoService.ListarPropiosAsync</c> (AC-06, defensa en
/// profundidad ante R-02 del threat model).
/// </summary>
public sealed record ListarBingosQuery(
    [Range(1, int.MaxValue)] int Page = 1,
    [Range(1, int.MaxValue)] int PageSize = 20);
