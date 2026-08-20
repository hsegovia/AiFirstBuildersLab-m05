namespace BingoCart.Application.Carritos.Dtos;

/// <summary>
/// Datos de un cartón resueltos contra SQL Server, listos para agregarse al carrito o mostrarse en
/// él (spec FEAT-008b, Block 2) — <see cref="Bingos.IBingoRepository.ObtenerParaCarritoAsync"/>
/// devuelve esto (o <c>null</c>) solo si el bingo del cartón sigue activo. <see cref="PrecioUnitario"/>
/// es el <c>CostoPorCarton</c> del bingo en el momento de la consulta — quien lo use para
/// <c>IntentarAgregarAsync</c> lo snapshottea (decisión de PLAN); quien lo use para mostrar el
/// carrito ya tiene el precio guardado y solo usa este DTO para el nombre de organización/evento.
/// </summary>
public sealed record CartonParaCarrito(
    Guid CartonId,
    Guid BingoId,
    decimal PrecioUnitario,
    string NombreOrganizacion,
    string NombreEvento);
