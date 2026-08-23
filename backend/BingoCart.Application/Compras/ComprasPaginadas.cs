namespace BingoCart.Application.Compras;

/// <summary>
/// Resultado de <see cref="ICompraRepository.ListarPorOrganizadorAsync"/>: la página de compras
/// solicitada (cada una emparejada con su monto ya calculado) junto con el total real de compras
/// del organizador (sin paginar) — spec FEAT-009c, Block 2. Mismo patrón de "record dedicado para
/// retorno correlacionado" que <see cref="BingoCart.Application.Bingos.BingosPaginados"/>.
/// </summary>
public sealed record ComprasPaginadas(IReadOnlyList<CompraConMonto> Items, int Total);
