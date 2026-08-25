using BingoCart.Application.Compras;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BingoCart.Infrastructure.Notificaciones;

/// <summary>
/// Implementa <see cref="ICartonPdfRenderer"/> (Application, Block 2 del spec FEAT-009b) vía
/// QuestPDF. Genera un PDF de una sola página por cartón, con sus 10 números, su número correlativo
/// dentro del bingo (FEAT-009d, Block 2 — FR-12/AC-04) y su GUID — el GUID se imprime
/// deliberadamente porque es la razón de ser de RF-06 (postpuesta): sin él, el PDF no serviría para
/// esa validación futura. El correlativo va destacado y el GUID en cuerpo chico: el primero es el
/// número con el que el comprador reconoce su cartón, el segundo es maquinaria.
/// </summary>
public sealed class QuestPdfCartonRenderer : ICartonPdfRenderer
{
    public byte[] Renderizar(Guid cartonId, int numeroCorrelativo, IReadOnlyList<int> numeros)
    {
        var documento = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A5);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(14));

                page.Content().Column(column =>
                {
                    column.Spacing(10);

                    column.Item().Text("Comprobante de cartón").FontSize(20).Bold();
                    column.Item().Text($"Cartón N° {numeroCorrelativo}").FontSize(16).Bold();
                    column.Item().Text($"ID: {cartonId}").FontSize(10);

                    column.Item().Row(row =>
                    {
                        foreach (var numero in numeros)
                        {
                            row.RelativeItem().Border(1).Padding(5).AlignCenter().Text(numero.ToString());
                        }
                    });
                });
            });
        });

        return documento.GeneratePdf();
    }
}
