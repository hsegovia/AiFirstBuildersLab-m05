namespace BingoCart.Domain.Organizadores;

/// <summary>
/// Validador puro de CUIT/CUIL argentino: formato (11 dígitos numéricos) y dígito verificador
/// según el algoritmo estándar (multiplicadores 5,4,3,2,7,6,5,4,3,2, módulo 11). Sin side effects
/// — no valida contra AFIP ni ningún padrón externo (FR-02, R-05 del PRD maestro).
/// </summary>
public static class CuitValidator
{
    private const int LongitudEsperada = 11;

    private static readonly int[] Multiplicadores = { 5, 4, 3, 2, 7, 6, 5, 4, 3, 2 };

    /// <summary>
    /// Verifica que el CUIT tenga exactamente 11 caracteres, todos dígitos numéricos.
    /// </summary>
    public static bool TieneLongitudValida(string cuit)
        => cuit.Length == LongitudEsperada && cuit.All(char.IsDigit);

    /// <summary>
    /// Verifica el dígito verificador (posición 11) contra el algoritmo estándar CUIT/CUIL.
    /// Precondición: <paramref name="cuit"/> ya tiene 11 dígitos numéricos
    /// (ver <see cref="TieneLongitudValida"/>).
    /// </summary>
    public static bool TieneDigitoVerificadorValido(string cuit)
    {
        var digitos = cuit.Select(c => c - '0').ToArray();

        var suma = 0;
        for (var i = 0; i < Multiplicadores.Length; i++)
        {
            suma += digitos[i] * Multiplicadores[i];
        }

        var resto = suma % 11;
        var digitoEsperado = 11 - resto;

        if (digitoEsperado == 11)
        {
            digitoEsperado = 0;
        }

        if (digitoEsperado == 10)
        {
            // Caso especial del algoritmo estándar: cuando el dígito esperado calcula 10, el
            // CUIT es inválido sin importar el dígito verificador provisto.
            return false;
        }

        return digitoEsperado == digitos[10];
    }

    public static bool EsValido(string cuit)
        => TieneLongitudValida(cuit) && TieneDigitoVerificadorValido(cuit);
}
