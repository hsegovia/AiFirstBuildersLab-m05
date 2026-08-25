namespace BingoCart.Application.Descubrimiento.Dtos;

/// <summary>
/// Un cartón descubierto al azar, ya unido a los datos públicos de su bingo (spec FEAT-008a,
/// Block 2) — la respuesta que ambos endpoints de <c>CartonesController</c> exponen. Nunca incluye
/// CUIT/mail/teléfono del organizador: solo <see cref="NombreOrganizacion"/>, mismo criterio de
/// exposición mínima que <c>DirectorioOrganizadorItem</c> (FEAT-005). Desde FEAT-009d (Block 2,
/// FR-12) incluye también <see cref="NumeroCorrelativo"/>, la posición 1..N del cartón dentro de su
/// bingo: dato de presentación, nunca un identificador direccionable (<see cref="Id"/> lo sigue
/// siendo, y es el único que las rutas aceptan — AC-13/AC-15).
/// </summary>
public sealed record CartonDescubiertoResponse(
    Guid Id,
    int NumeroCorrelativo,
    string NombreOrganizacion,
    string NombreEvento,
    DateTime FechaSorteoUtc,
    decimal CostoPorCarton,
    IReadOnlyList<int> Numeros);
