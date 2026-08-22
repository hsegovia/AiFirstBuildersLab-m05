namespace BingoCart.Application.Compras.Dtos;

/// <summary>
/// Datos resueltos para armar el mail de cancelación de una compra (spec FEAT-009c, Block 2) —
/// deliberadamente mínimo (sin detalle de cartones/monto, a diferencia de
/// <see cref="DatosParaMailConfirmacion"/>): un aviso de cancelación no necesita repetir el detalle
/// de una compra que ya no es válida.
/// </summary>
public sealed record DatosParaMailCancelacion(
    string MailComprador,
    string NombreComprador,
    string ApellidoComprador,
    Guid CompraId,
    string NombreOrganizacion);
