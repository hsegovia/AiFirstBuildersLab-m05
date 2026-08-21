namespace BingoCart.Application.Compras;

/// <summary>
/// Puerto de generación del comprobante PDF de un cartón (spec FEAT-009b, Block 2). Infrastructure
/// lo implementa en Block 3 vía QuestPDF.
/// </summary>
public interface ICartonPdfRenderer
{
    /// <summary>
    /// Genera el PDF de una página de <paramref name="cartonId"/>, mostrando sus 10
    /// <paramref name="numeros"/> y su GUID — impreso porque es la razón de ser de RF-06
    /// (postpuesta), sin la cual el PDF no serviría para esa validación futura.
    /// </summary>
    byte[] Renderizar(Guid cartonId, IReadOnlyList<int> numeros);
}
