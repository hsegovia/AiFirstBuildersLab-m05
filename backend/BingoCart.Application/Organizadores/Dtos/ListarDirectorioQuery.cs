using System.ComponentModel.DataAnnotations;

namespace BingoCart.Application.Organizadores.Dtos;

/// <summary>
/// Query de listado paginado del directorio público de organizadores (spec FEAT-005, Block 2).
/// Record posicional, mismo estilo que <see cref="Bingos.Dtos.ListarBingosQuery"/> (decisión de
/// PLAN): sin invariante de dominio que proteger — la paginación no es una regla de negocio — así
/// que el 400 automático de <c>InvalidModelStateResponseFactory</c> (ya registrado en
/// <c>Program.cs</c>) es exactamente lo que pide AC-06, sin duplicar ese mecanismo a mano.
/// <c>PageSize</c> &gt; 100 NO se rechaza acá — se clampea en
/// <c>OrganizadorService.ListarDirectorioAsync</c> (AC-05, defensa en profundidad ante R-02 del
/// threat model).
/// </summary>
public sealed record ListarDirectorioQuery(
    [Range(1, int.MaxValue)] int Page = 1,
    [Range(1, int.MaxValue)] int PageSize = 20);
