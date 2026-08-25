namespace BingoCart.Application.Compras.Dtos;

/// <summary>
/// Un cartón adquirido por el comprador autenticado, tal como lo devuelve el listado paginado
/// "mis cartones" (spec FEAT-009d, Block 3, FR-11/AC-01). A diferencia de
/// <see cref="CartonCompradoResponse"/> (el acuse breve de la confirmación), este DTO lleva el
/// detalle completo que el comprador necesita para reconocer y consultar cada cartón que compró:
/// sus <see cref="Numeros"/>, el bingo y la organización a los que pertenece, y el
/// <see cref="EstadoCompra"/> de la compra que lo incluye — el nombre del campo, no "Estado", porque
/// describe el estado de LA COMPRA, no del cartón (un cartón no tiene estado propio).
/// <see cref="NombreBingo"/> (no <c>NombreEvento</c>, el nombre de campo que usa el resto del
/// proyecto) es el nombre exacto que fija el API contract de este bloque.
/// </summary>
public sealed record CartonAdquiridoResponse(
    Guid CartonId,
    int NumeroCorrelativo,
    IReadOnlyList<int> Numeros,
    string NombreBingo,
    string NombreOrganizacion,
    string EstadoCompra,
    Guid CompraId);
