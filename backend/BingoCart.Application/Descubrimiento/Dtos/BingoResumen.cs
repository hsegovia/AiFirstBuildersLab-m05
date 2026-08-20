namespace BingoCart.Application.Descubrimiento.Dtos;

/// <summary>
/// Resumen de un bingo con el nombre de su organizador (spec FEAT-008a, Block 1) — usado por
/// Application (Block 2) para armar <c>CartonDescubiertoResponse</c> uniendo cada <c>Carton</c>
/// aleatorio con los datos de su bingo. A diferencia de <c>DirectorioOrganizadorItem</c> (FEAT-005),
/// que lista organizadores paginados, este DTO resuelve un conjunto acotado de <c>bingoId</c> ya
/// conocidos (como máximo 5, uno por cartón devuelto) — no pagina ni filtra por "activo".
/// </summary>
public sealed record BingoResumen(Guid Id, string NombreOrganizacion, string NombreEvento, decimal CostoPorCarton, DateTime FechaSorteoUtc);
