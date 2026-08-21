namespace BingoCart.Application.Compras.Dtos;

/// <summary>
/// Datos resueltos para armar el mail de confirmación de una confirmación de carrito (spec
/// FEAT-009b, Block 2) — agrupa todas las <see cref="CompraParaMail"/> que comparten un mismo
/// <c>ConfirmacionId</c>, para un único mail en lugar de uno por organizador (AC-01/AC-02).
/// </summary>
public sealed record DatosParaMailConfirmacion(
    string MailComprador,
    string NombreComprador,
    string ApellidoComprador,
    IReadOnlyList<CompraParaMail> Compras);
