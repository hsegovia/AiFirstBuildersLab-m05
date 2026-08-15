using BingoCart.Domain.Organizadores;
using BingoCart.Domain.Organizadores.Exceptions;

namespace BingoCart.Domain.Tests.Organizadores;

public class OrganizadorTests
{
    // CUIT real verificado a mano con el algoritmo estándar CUIT/CUIL (multiplicadores
    // 5,4,3,2,7,6,5,4,3,2, módulo 11): suma=64, resto=9, dígito esperado=11-9=2, coincide con el
    // último dígito (2). Ver reporte del bloque para el cálculo completo.
    private const string CuitValido = "30500010912";
    private const string TelefonoValido = "+54 11 4444-5555";

    [Fact]
    public void Crear_ConDatosValidos_CreaLaEntidad()
    {
        var organizador = Organizador.Crear(
            "Club Social y Deportivo",
            CuitValido,
            "contacto@club.org",
            TelefonoValido);

        Assert.NotEqual(Guid.Empty, organizador.Id);
        Assert.Equal("Club Social y Deportivo", organizador.NombreOrganizacion);
        Assert.Equal(CuitValido, organizador.Cuit);
        Assert.Equal("contacto@club.org", organizador.Mail);
        Assert.Equal(TelefonoValido, organizador.Telefono);
    }

    [Fact]
    public void Crear_ConCuitDeLongitudIncorrecta_LanzaCuitInvalidoException()
    {
        var ex = Assert.Throws<CuitInvalidoException>(() =>
            Organizador.Crear("Club Social", "12345", "contacto@club.org", TelefonoValido));

        Assert.DoesNotContain("12345", ex.Message);
    }

    [Fact]
    public void Crear_ConDigitoVerificadorInvalido_LanzaCuitInvalidoException()
    {
        // Mismos 11 dígitos que CuitValido pero con el dígito verificador alterado (2 -> 0).
        const string cuitDigitoAlterado = "30500010910";

        var ex = Assert.Throws<CuitInvalidoException>(() =>
            Organizador.Crear("Club Social", cuitDigitoAlterado, "contacto@club.org", TelefonoValido));

        Assert.DoesNotContain(cuitDigitoAlterado, ex.Message);
    }

    [Fact]
    public void Crear_ConTelefonoNoNumerico_LanzaTelefonoInvalidoException()
    {
        const string telefonoInvalido = "abc12345defg";

        var ex = Assert.Throws<TelefonoInvalidoException>(() =>
            Organizador.Crear("Club Social", CuitValido, "contacto@club.org", telefonoInvalido));

        Assert.DoesNotContain(telefonoInvalido, ex.Message);
    }

    [Theory]
    [InlineData("1234567")] // 7 caracteres, por debajo del mínimo de 8
    [InlineData("123456789012345678901")] // 21 caracteres, por encima del máximo de 20
    public void Crear_ConTelefonoFueraDeRango_LanzaTelefonoInvalidoException(string telefonoInvalido)
    {
        Assert.Throws<TelefonoInvalidoException>(() =>
            Organizador.Crear("Club Social", CuitValido, "contacto@club.org", telefonoInvalido));
    }
}
