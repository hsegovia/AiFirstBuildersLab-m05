using System;
using System.Linq;
using System.Text;
using BingoCart.Infrastructure.Notificaciones;
using QuestPDF.Infrastructure;

namespace BingoCart.Infrastructure.Tests.Notificaciones;

/// <summary>
/// Tests unitarios de <see cref="QuestPdfCartonRenderer"/> (spec FEAT-009b, Block 3) — sin I/O
/// externo (a diferencia de <c>EnvioMailRepositoryTests</c>/<c>MailKitEmailSenderTests</c>), QuestPDF
/// genera el PDF en memoria.
/// </summary>
public sealed class QuestPdfCartonRendererTests
{
    static QuestPdfCartonRendererTests()
    {
        // Bootstrap de licencia Community requerido una sola vez por proceso (igual que Program.cs,
        // spec Block 3) — el proceso de test no ejecuta Program.cs, así que este test lo hace por
        // su cuenta.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    [Fact]
    public void Renderizar_DevuelvePdfNoVacioConFirmaValida()
    {
        var renderer = new QuestPdfCartonRenderer();
        var numeros = Enumerable.Range(1, 10).ToList();

        var pdf = renderer.Renderizar(Guid.NewGuid(), numeros);

        Assert.NotEmpty(pdf);
        var firma = Encoding.ASCII.GetString(pdf, 0, 4);
        Assert.Equal("%PDF", firma);
    }
}
