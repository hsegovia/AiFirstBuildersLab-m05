using BingoCart.Domain.Organizadores;

namespace BingoCart.Domain.Tests.Organizadores;

public class CuitValidatorTests
{
    // CUIT real verificado a mano (ver cálculo en OrganizadorTests y en el reporte del bloque).
    private const string CuitValido = "30500010912";

    [Fact]
    public void EsValido_ConCuitValidoConocido_DevuelveTrue()
    {
        Assert.True(CuitValidator.EsValido(CuitValido));
    }

    [Theory]
    [InlineData("3050001091")] // 10 dígitos
    [InlineData("305000109123")] // 12 dígitos
    public void EsValido_ConLongitudIncorrecta_DevuelveFalse(string cuit)
    {
        Assert.False(CuitValidator.EsValido(cuit));
    }

    [Fact]
    public void TieneLongitudValida_ConCuitDeLongitudCorrecta_DevuelveTrue()
    {
        Assert.True(CuitValidator.TieneLongitudValida(CuitValido));
    }

    [Theory]
    [InlineData("3050001091")]
    [InlineData("305000109123")]
    [InlineData("3050001091a")] // 11 caracteres pero no numérico
    public void TieneLongitudValida_ConLongitudOFormatoIncorrecto_DevuelveFalse(string cuit)
    {
        Assert.False(CuitValidator.TieneLongitudValida(cuit));
    }

    [Fact]
    public void EsValido_ConDigitoVerificadorAlteradoEnUnaPosicion_DevuelveFalse()
    {
        // Mismo CUIT válido pero con el último dígito (verificador) cambiado de 2 a 0.
        const string cuitDigitoAlterado = "30500010910";

        Assert.False(CuitValidator.EsValido(cuitDigitoAlterado));
    }

    [Fact]
    public void TieneDigitoVerificadorValido_ConRestoDiezSegunAlgoritmo_DevuelveFalseSiempre()
    {
        // Primeros 10 dígitos elegidos para que la suma ponderada dé resto=1 (mod 11), lo que
        // implica dígito verificador esperado = 11 - 1 = 10: caso especial del algoritmo en el
        // que el CUIT es inválido sin importar el último dígito.
        const string cuitRestoDiez = "20000000010";

        Assert.False(CuitValidator.TieneDigitoVerificadorValido(cuitRestoDiez));
    }

    [Fact]
    public void TieneDigitoVerificadorValido_ConCuitValido_DevuelveTrue()
    {
        Assert.True(CuitValidator.TieneDigitoVerificadorValido(CuitValido));
    }

    [Fact]
    public void TieneDigitoVerificadorValido_ConRestoCero_ReduceOnceAZeroYDevuelveTrue()
    {
        // Primeros 10 dígitos en cero: suma ponderada = 0, resto = 0, dígito esperado = 11 - 0 =
        // 11, que el algoritmo reduce a 0 (caso especial "resto 11 -> dígito verificador 0").
        // Con el último dígito en 0, coincide y el CUIT resulta válido según el algoritmo.
        const string cuitRestoCero = "00000000000";

        Assert.True(CuitValidator.TieneDigitoVerificadorValido(cuitRestoCero));
    }
}
