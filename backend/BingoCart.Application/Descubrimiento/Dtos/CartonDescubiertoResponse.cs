namespace BingoCart.Application.Descubrimiento.Dtos;

/// <summary>
/// Un cartón descubierto al azar, ya unido a los datos públicos de su bingo (spec FEAT-008a,
/// Block 2) — la respuesta que ambos endpoints de <c>CartonesController</c> exponen. Nunca incluye
/// CUIT/mail/teléfono del organizador: solo <see cref="NombreOrganizacion"/>, mismo criterio de
/// exposición mínima que <c>DirectorioOrganizadorItem</c> (FEAT-005).
/// </summary>
public sealed record CartonDescubiertoResponse(
    Guid Id,
    string NombreOrganizacion,
    string NombreEvento,
    DateTime FechaSorteoUtc,
    decimal CostoPorCarton,
    IReadOnlyList<int> Numeros);
