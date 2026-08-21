using BingoCart.Domain.Compradores.Exceptions;
using BingoCart.Domain.Organizadores;

namespace BingoCart.Domain.Compradores;

/// <summary>
/// Entidad de dominio: comprador de cartones (FEAT-009a). Espeja <see
/// cref="Organizadores.Organizador"/> — inmutable tras su creación, sin setters públicos; la única
/// forma de construirla es <see cref="Crear"/>, que reutiliza <see cref="CuitValidator"/> (Domain.
/// Organizadores, clase estática sin side effects) en vez de duplicar el algoritmo de CUIT/CUIL.
/// </summary>
public sealed class Comprador
{
    public Guid Id { get; private init; }

    public string Apellido { get; private init; } = string.Empty;

    public string Nombre { get; private init; } = string.Empty;

    public string Cuit { get; private init; } = string.Empty;

    public string Mail { get; private init; } = string.Empty;

    private Comprador()
    {
    }

    /// <summary>
    /// Crea un <see cref="Comprador"/> validando CUIT. La unicidad del mail y la política de
    /// password NO se validan acá: son responsabilidad de Application (Block 2), que consulta
    /// <c>ICompradorIdentityGateway</c> antes y después de invocar este factory.
    /// </summary>
    /// <exception cref="CuitInvalidoException">
    /// El CUIT no tiene 11 dígitos numéricos o su dígito verificador no es válido.
    /// </exception>
    public static Comprador Crear(string apellido, string nombre, string cuit, string mail)
    {
        if (!CuitValidator.TieneLongitudValida(cuit))
        {
            throw new CuitInvalidoException("El CUIT debe tener exactamente 11 dígitos numéricos.");
        }

        if (!CuitValidator.TieneDigitoVerificadorValido(cuit))
        {
            throw new CuitInvalidoException("El dígito verificador del CUIT ingresado no es válido.");
        }

        return new Comprador
        {
            Id = Guid.NewGuid(),
            Apellido = apellido,
            Nombre = nombre,
            Cuit = cuit,
            Mail = mail,
        };
    }
}
