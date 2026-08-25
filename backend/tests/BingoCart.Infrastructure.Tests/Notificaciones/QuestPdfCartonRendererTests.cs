using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BingoCart.Infrastructure.Notificaciones;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
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

        var pdf = renderer.Renderizar(Guid.NewGuid(), numeroCorrelativo: 1, numeros);

        Assert.NotEmpty(pdf);
        var firma = Encoding.ASCII.GetString(pdf, 0, 4);
        Assert.Equal("%PDF", firma);
    }

    /// <summary>
    /// Spec FEAT-009d, Block 2 (FR-12/AC-04): el documento tiene que mostrar EL correlativo del
    /// cartón, no un número cualquiera derivado de él. El texto de un PDF de QuestPDF/SkiaSharp no
    /// viaja como caracteres sino como identificadores de glifo de la fuente embebida, así que
    /// buscar la cadena "42" en los bytes no sirve. Lo que sí sirve es compararlo contra un
    /// DOCUMENTO DE CONTROL renderizado por el mismo QuestPDF con el texto esperado ya armado
    /// ("Cartón N° 42") y con la misma fuente y tamaño: produce exactamente la secuencia de glifos
    /// que el renderer debería dibujar. La comparación es concluyente en las dos direcciones: la
    /// primera aserción exige que esa secuencia esté entre las que el renderer dibuja —si imprimiera
    /// otro número, o ninguno, no estaría—, y la segunda exige que la del correlativo vecino NO
    /// esté, que es lo que descarta que el "match" sea trivial (si lo fuera, las dos pasarían).
    /// </summary>
    [Fact]
    public void Renderizar_IncluyeElNumeroCorrelativoEnElDocumento()
    {
        var renderer = new QuestPdfCartonRenderer();
        var cartonId = Guid.NewGuid();
        var numeros = Enumerable.Range(1, 10).ToList();

        var dibujadas = SecuenciasDeTextoDibujadas(renderer.Renderizar(cartonId, 42, numeros));

        Assert.Contains(SecuenciaDeControl("Cartón N° 42"), dibujadas);
        Assert.DoesNotContain(SecuenciaDeControl("Cartón N° 43"), dibujadas);
    }

    // Renderiza el texto esperado, solo, con la misma fuente y tamaño que el renderer usa para el
    // correlativo (16, negrita), y devuelve su única secuencia de glifos: el patrón contra el que se
    // compara lo que el renderer dibujó.
    private static string SecuenciaDeControl(string textoEsperado)
    {
        var control = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A5);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(14));
                page.Content().Text(textoEsperado).FontSize(16).Bold();
            });
        }).GeneratePdf();

        return Assert.Single(SecuenciasDeTextoDibujadas(control));
    }

    // Devuelve una secuencia de glifos por cada operador de dibujo de texto (`[...] TJ`) del PDF:
    // los códigos hexadecimales del arreglo, concatenados y sin los ajustes de kerning intercalados
    // —esos dependen de la posición del texto en la página, no de su contenido, y harían fallar la
    // comparación entre el documento real y el de control—.
    private static IReadOnlyList<string> SecuenciasDeTextoDibujadas(byte[] pdf)
    {
        var secuencias = new List<string>();

        foreach (Match operador in Regex.Matches(FlujosDescomprimidos(pdf), @"\[(.*?)\]\s*TJ", RegexOptions.Singleline))
        {
            var glifos = new StringBuilder();
            foreach (Match glifo in Regex.Matches(operador.Groups[1].Value, "<([0-9A-Fa-f]+)>"))
            {
                glifos.Append(glifo.Groups[1].Value);
            }

            secuencias.Add(glifos.ToString());
        }

        return secuencias;
    }

    // Descomprime y concatena TODOS los flujos FlateDecode del PDF (entre ellos el de contenido, con
    // las órdenes de dibujo de la página). Deja afuera lo que no descomprime como zlib —fuentes
    // embebidas y otros recursos binarios—, que no aporta órdenes de dibujo.
    private static string FlujosDescomprimidos(byte[] pdf)
    {
        // Latin1 y no UTF8 a propósito: es la única codificación de un byte por carácter sin
        // reemplazos, así que el índice de cada char coincide con el offset del byte que le
        // corresponde en `pdf`, y por eso los índices que devuelve IndexOf se pueden usar tal cual
        // sobre el arreglo de bytes. Con UTF8 los offsets se desincronizan en cuanto aparece un byte
        // >= 0x80 (los flujos son binarios: aparecen siempre) y sin ningún error visible.
        var texto = Encoding.Latin1.GetString(pdf);
        var contenido = new StringBuilder();
        var posicion = 0;

        while (true)
        {
            var inicioMarcador = texto.IndexOf("stream", posicion, StringComparison.Ordinal);
            if (inicioMarcador < 0)
            {
                break;
            }

            var inicioDatos = inicioMarcador + "stream".Length;
            while (inicioDatos < texto.Length && (texto[inicioDatos] == '\r' || texto[inicioDatos] == '\n'))
            {
                inicioDatos++;
            }

            var fin = texto.IndexOf("endstream", inicioDatos, StringComparison.Ordinal);
            if (fin < 0)
            {
                break;
            }

            try
            {
                using var comprimido = new MemoryStream(pdf, inicioDatos, fin - inicioDatos);
                using var descompresor = new ZLibStream(comprimido, CompressionMode.Decompress);
                using var descomprimido = new MemoryStream();
                descompresor.CopyTo(descomprimido);
                contenido.Append(Encoding.Latin1.GetString(descomprimido.ToArray()));
            }
            catch (InvalidDataException)
            {
                // No es un flujo zlib (fuente embebida u otro recurso binario): no aporta órdenes
                // de dibujo, se ignora.
            }

            // Salta el marcador "endstream" ENTERO: avanzar de a un carácter dejaría la posición
            // dentro de la palabra, y el IndexOf("stream") siguiente matchearía el "stream" de ese
            // mismo "endstream" en vez del flujo siguiente — el recorrido nunca pasaría del primero.
            posicion = fin + "endstream".Length;
        }

        return contenido.ToString();
    }
}
