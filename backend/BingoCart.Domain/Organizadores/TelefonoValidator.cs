using System.Text.RegularExpressions;

namespace BingoCart.Domain.Organizadores;

/// <summary>
/// Validador puro de teléfono: numérico (dígitos, '+', espacios y guiones permitidos), longitud
/// entre 8 y 20 caracteres (FR-05). Sin side effects.
/// </summary>
public static class TelefonoValidator
{
    private const int LongitudMinima = 8;
    private const int LongitudMaxima = 20;

    private static readonly Regex PatronNumerico = new(@"^[0-9+\-\s]+$", RegexOptions.Compiled);

    public static bool EsValido(string telefono)
        => telefono.Length >= LongitudMinima
           && telefono.Length <= LongitudMaxima
           && PatronNumerico.IsMatch(telefono);
}
