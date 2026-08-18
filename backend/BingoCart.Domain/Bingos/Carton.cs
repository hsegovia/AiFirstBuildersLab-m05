using BingoCart.Domain.Bingos.Exceptions;

namespace BingoCart.Domain.Bingos;

/// <summary>
/// Entidad de dominio: un cartón de un bingo (FEAT-003). Inmutable tras su creación — no expone
/// setters públicos; la única forma de construirla es <see cref="Crear"/>, que valida que el
/// conjunto de números tenga exactamente 10 elementos distintos entre sí y todos entre 1 y 90
/// (invariante propia del cartón — un cartón de bingo es por definición eso, independientemente de
/// qué componente generó los números). La unicidad ENTRE cartones del mismo bingo sigue siendo
/// responsabilidad del generador CSPRNG (Block 2); el rango sí lo garantiza este factory, para que
/// sea estructuralmente imposible construir un <see cref="Carton"/> con un número fuera de rango.
/// </summary>
public sealed class Carton
{
    public Guid Id { get; private init; }

    public Guid BingoId { get; private init; }

    public IReadOnlyList<int> Numeros { get; private init; } = Array.Empty<int>();

    private Carton()
    {
    }

    /// <summary>
    /// Crea un <see cref="Carton"/> validando que <paramref name="numeros"/> tenga exactamente 10
    /// números distintos entre sí. Los números se normalizan en orden ascendente antes de
    /// asignarse, para que la representación canónica sea determinística.
    /// </summary>
    /// <exception cref="NumerosCartonInvalidosException">
    /// <paramref name="numeros"/> no tiene exactamente 10 elementos, contiene duplicados, o algún
    /// número está fuera del rango 1-90.
    /// </exception>
    public static Carton Crear(Guid bingoId, IReadOnlyList<int> numeros)
    {
        var sinDuplicados = new HashSet<int>(numeros).Count == numeros.Count;
        var todosEnRango = numeros.All(n => n is >= 1 and <= 90);

        if (numeros.Count != 10 || !sinDuplicados || !todosEnRango)
        {
            throw new NumerosCartonInvalidosException(
                "Un cartón debe tener exactamente 10 números distintos entre 1 y 90.");
        }

        return new Carton
        {
            Id = Guid.NewGuid(),
            BingoId = bingoId,
            Numeros = numeros.OrderBy(n => n).ToList(),
        };
    }
}
