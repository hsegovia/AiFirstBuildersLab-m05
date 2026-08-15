using BingoCart.Domain.Organizadores;

namespace BingoCart.Domain.Tests.Organizadores;

public class TelefonoValidatorTests
{
    [Theory]
    [InlineData("12345678")] // exactamente 8 caracteres (límite mínimo)
    [InlineData("12345678901234567890")] // exactamente 20 caracteres (límite máximo)
    [InlineData("+54 11 4444-5555")] // dígitos, '+', espacios y guiones
    [InlineData("011-4444-5555")]
    public void EsValido_ConFormatoYLongitudCorrectos_DevuelveTrue(string telefono)
    {
        Assert.True(TelefonoValidator.EsValido(telefono));
    }

    [Theory]
    [InlineData("1234567")] // 7 caracteres, por debajo del mínimo
    [InlineData("123456789012345678901")] // 21 caracteres, por encima del máximo
    [InlineData("1234abc8")] // contiene letras
    [InlineData("1234567*")] // contiene un carácter no permitido
    public void EsValido_ConFormatoOLongitudIncorrectos_DevuelveFalse(string telefono)
    {
        Assert.False(TelefonoValidator.EsValido(telefono));
    }
}
