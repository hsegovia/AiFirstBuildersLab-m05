using System.Security.Cryptography;
using BingoCart.Application.Bingos;

namespace BingoCart.Infrastructure.Bingos;

/// <summary>
/// Única clase del sistema que genera los números de un cartón de bingo (spec FEAT-003, Block 2).
/// Usa exclusivamente <see cref="RandomNumberGenerator"/> (CSPRNG) — ninguna fuente de
/// aleatoriedad no criptográfica — para satisfacer NFR-02. Cada conjunto se produce con un Fisher-Yates parcial sobre [1..90]:
/// baraja solo las primeras 10 posiciones que hacen falta (no el arreglo completo), lo que evita
/// el rechazo de duplicados DENTRO de un mismo cartón por construcción — el rechazo que sí puede
/// ocurrir es ENTRE cartones ya generados en la misma llamada (ver <see cref="GenerarConjuntosUnicos"/>).
/// </summary>
public sealed class CartonNumberGenerator : ICartonNumberGenerator
{
    private const int MinNumero = 1;
    private const int MaxNumero = 90;
    private const int NumerosPorCarton = 10;
    private const int MaxReintentosPorColision = 10;

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">
    /// Un conjunto colisionó con uno ya generado en esta misma llamada y los
    /// <see cref="MaxReintentosPorColision"/> reintentos de regeneración también colisionaron —
    /// astronómicamente improbable (hay C(90,10) ≈ 5,72 × 10¹² combinaciones posibles), no se
    /// espera que ocurra nunca en la práctica con cantidades ≤ 5.000 (ver spec).
    /// </exception>
    public IReadOnlyList<IReadOnlyList<int>> GenerarConjuntosUnicos(int cantidad)
    {
        var conjuntos = new List<IReadOnlyList<int>>(cantidad);
        var generados = new HashSet<string>(cantidad);

        for (var i = 0; i < cantidad; i++)
        {
            conjuntos.Add(GenerarConjuntoUnico(generados));
        }

        return conjuntos;
    }

    private static IReadOnlyList<int> GenerarConjuntoUnico(HashSet<string> generados)
    {
        for (var intento = 0; intento < MaxReintentosPorColision; intento++)
        {
            var conjunto = GenerarConjunto();
            var claveCanonica = string.Join(",", conjunto);

            if (generados.Add(claveCanonica))
            {
                return conjunto;
            }
        }

        throw new InvalidOperationException(
            $"No se pudo generar un conjunto de números de cartón único tras " +
            $"{MaxReintentosPorColision} reintentos consecutivos por colisión. Este caso no " +
            "debería ocurrir nunca en la práctica (hay C(90,10) ≈ 5,72 × 10¹² combinaciones " +
            "posibles); si ocurre, revisar la fuente de aleatoriedad.");
    }

    private static IReadOnlyList<int> GenerarConjunto()
    {
        var numeros = new int[MaxNumero];
        for (var i = 0; i < MaxNumero; i++)
        {
            numeros[i] = MinNumero + i;
        }

        for (var i = 0; i < NumerosPorCarton; i++)
        {
            var j = RandomNumberGenerator.GetInt32(i, MaxNumero);
            (numeros[i], numeros[j]) = (numeros[j], numeros[i]);
        }

        var conjunto = numeros[..NumerosPorCarton];
        Array.Sort(conjunto);
        return conjunto;
    }
}
