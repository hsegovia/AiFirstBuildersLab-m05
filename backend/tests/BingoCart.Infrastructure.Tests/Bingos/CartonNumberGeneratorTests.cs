using System.Diagnostics;
using BingoCart.Application.Bingos;
using BingoCart.Infrastructure.Bingos;

namespace BingoCart.Infrastructure.Tests.Bingos;

/// <summary>
/// Tests unitarios de <see cref="CartonNumberGenerator"/> (spec FEAT-003, Block 2). Verifican que
/// cada conjunto generado tiene exactamente 10 números distintos entre 1 y 90, y que conjuntos
/// generados dentro de la misma llamada son distintos ENTRE SÍ (comparación por conjunto ordenado
/// completo, no elemento por elemento) — FR-03/FR-05/NFR-02.
/// </summary>
public class CartonNumberGeneratorTests
{
    private static readonly ICartonNumberGenerator Generator = new CartonNumberGenerator();

    [Fact]
    public void GenerarConjuntosUnicos_Con1_DevuelveUnConjuntoDeDiezNumerosValidos()
    {
        var resultado = Generator.GenerarConjuntosUnicos(1);

        Assert.Single(resultado);

        var conjunto = resultado[0];
        Assert.Equal(10, conjunto.Count);
        Assert.All(conjunto, n => Assert.InRange(n, 1, 90));
        Assert.Equal(conjunto.Count, new HashSet<int>(conjunto).Count);
    }

    [Fact]
    public void GenerarConjuntosUnicos_Con100_TodosLosConjuntosSonDistintosEntreSi()
    {
        var resultado = Generator.GenerarConjuntosUnicos(100);

        Assert.Equal(100, resultado.Count);

        var canonicos = resultado
            .Select(conjunto => string.Join(",", conjunto.OrderBy(n => n)))
            .ToHashSet();

        Assert.Equal(100, canonicos.Count);
    }

    [Fact]
    public void GenerarConjuntosUnicos_Con5000_CompletaSinExcepcionYTodosSonDistintosEntreSi()
    {
        var cronometro = Stopwatch.StartNew();
        var resultado = Generator.GenerarConjuntosUnicos(5000);
        cronometro.Stop();

        Assert.Equal(5000, resultado.Count);

        var canonicos = resultado
            .Select(conjunto => string.Join(",", conjunto.OrderBy(n => n)))
            .ToHashSet();

        Assert.Equal(5000, canonicos.Count);

        // Sin aserción sobre el tiempo (no es el objeto de este test, ver NFR-01 en Block 4) —
        // se deja constancia en consola para el reporte del bloque.
        Console.WriteLine($"GenerarConjuntosUnicos(5000) tardó {cronometro.ElapsedMilliseconds} ms.");
    }
}
