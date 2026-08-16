using BingoCart.Domain.Organizadores.Exceptions;

namespace BingoCart.Domain.Organizadores;

/// <summary>
/// Entidad de dominio: organizador de bingos (FEAT-001a). Inmutable tras su creación — no expone
/// setters públicos; la única forma de construirla es <see cref="Crear"/>, que aplica las
/// invariantes de CUIT y teléfono (FR-02, FR-05).
/// </summary>
public sealed class Organizador
{
    public Guid Id { get; private init; }

    public string NombreOrganizacion { get; private init; } = string.Empty;

    public string Cuit { get; private init; } = string.Empty;

    public string Mail { get; private init; } = string.Empty;

    public string Telefono { get; private init; } = string.Empty;

    private Organizador()
    {
    }

    /// <summary>
    /// Crea un <see cref="Organizador"/> validando CUIT y teléfono. La unicidad del mail y la
    /// política de password NO se validan acá: son responsabilidad de Application (Block 3),
    /// que consulta <c>IIdentityGateway</c> antes y después de invocar este factory.
    /// </summary>
    /// <exception cref="CuitInvalidoException">
    /// El CUIT no tiene 11 dígitos numéricos o su dígito verificador no es válido.
    /// </exception>
    /// <exception cref="TelefonoInvalidoException">
    /// El teléfono no es numérico (permitiendo '+', espacios y guiones) o su longitud no está
    /// entre 8 y 20 caracteres.
    /// </exception>
    public static Organizador Crear(string nombreOrganizacion, string cuit, string mail, string telefono)
    {
        if (!CuitValidator.TieneLongitudValida(cuit))
        {
            throw new CuitInvalidoException("El CUIT debe tener exactamente 11 dígitos numéricos.");
        }

        if (!CuitValidator.TieneDigitoVerificadorValido(cuit))
        {
            throw new CuitInvalidoException("El dígito verificador del CUIT ingresado no es válido.");
        }

        if (!TelefonoValidator.EsValido(telefono))
        {
            throw new TelefonoInvalidoException(
                "El teléfono debe ser numérico (se permiten '+', espacios y guiones) y tener entre 8 y 20 caracteres.");
        }

        return new Organizador
        {
            Id = Guid.NewGuid(),
            NombreOrganizacion = nombreOrganizacion,
            Cuit = cuit,
            Mail = mail,
            Telefono = telefono,
        };
    }
}
