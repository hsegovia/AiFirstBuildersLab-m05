namespace BingoCart.Application.Bingos.Dtos;

/// <summary>
/// Confirmación de creación de un bingo. Deliberadamente SIN los cartones individuales — no hay
/// ningún AC que exija devolverlos, y 5.000 cartones en un solo JSON de respuesta es impracticable
/// (spec FEAT-003, Block 4). Quedan disponibles para consulta en un ticket futuro ("Mis bingos").
/// </summary>
public sealed record BingoCreadoResponse(
    Guid Id,
    string NombreEvento,
    DateTime FechaSorteoUtc,
    int CantidadCartones,
    decimal CostoPorCarton);
