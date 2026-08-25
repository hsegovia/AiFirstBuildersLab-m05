namespace BingoCart.Application.Compras.Dtos;

/// <summary>
/// Una <c>Compra</c> ya persistida, resultado de agrupar el carrito por organizador (spec FEAT-009a,
/// Block 2). <c>MontoTotal</c> es la suma de <c>PrecioUnitario</c> de todos sus ítems. Desde
/// FEAT-009d (Block 2, FR-12) suma <see cref="Cartones"/>: la respuesta de confirmación es una de
/// las superficies donde el comprador tiene que ver el correlativo de cada cartón, y hasta acá solo
/// devolvía cuántos eran. El campo se agrega al final y nada de lo anterior cambia, así que un
/// consumidor que lo ignore sigue funcionando igual.
/// </summary>
public sealed record CompraCreada(
    Guid CompraId,
    Guid OrganizadorId,
    string NombreOrganizacion,
    int CantidadCartones,
    decimal MontoTotal,
    IReadOnlyList<CartonCompradoResponse> Cartones);
