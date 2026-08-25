namespace BingoCart.Application.Compradores;

/// <summary>
/// Puerto de consulta de "sorteo inminente" para la regla de los 60 minutos (spec FEAT-009d,
/// Block 5, FR-07/NFR-05/D-01/D-02). Puerto PROPIO, deliberadamente NO agregado a
/// <c>ICompraRepository</c> (decisión D-03: las dos mitades del ticket —"cartones" y "cuenta"— se
/// implementan en archivos separados, sin compartir puertos).
/// </summary>
public interface ICompradorCuentaRepository
{
    /// <summary>
    /// Indica si <paramref name="compradorId"/> tiene al menos un cartón vigente cuyo sorteo
    /// (<c>Bingo.FechaSorteoUtc</c>) cae en la ventana <c>[ahoraUtc, hastaUtc]</c> — ambos límites
    /// inclusive (D-01: los sorteos ya pasados NO bloquean). Solo considera compras cuyo
    /// <c>Estado</c> es distinto de <c>Cancelado</c> (D-02): una compra cancelada ya no tiene
    /// cartones vigentes y no puede generar la disputa que esta regla previene.
    /// <paramref name="ahoraUtc"/>/<paramref name="hastaUtc"/> se asumen ya calculados por el
    /// llamador (Application, <c>CompradorService</c>, vía <c>TimeProvider</c>) — este puerto no
    /// decide la duración de la ventana, solo filtra contra los límites recibidos.
    /// </summary>
    Task<bool> TieneSorteoInminenteAsync(Guid compradorId, DateTime ahoraUtc, DateTime hastaUtc);
}
