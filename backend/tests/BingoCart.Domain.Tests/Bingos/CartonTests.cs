using BingoCart.Domain.Bingos;
using BingoCart.Domain.Bingos.Exceptions;

namespace BingoCart.Domain.Tests.Bingos;

public class CartonTests
{
    private static readonly Guid BingoId = Guid.NewGuid();

    [Fact]
    public void Crear_ConDiezNumerosValidosSinOrdenar_CreaElCartonConNumerosEnOrdenAscendente()
    {
        var numeros = new List<int> { 45, 3, 90, 12, 68, 71, 80, 85, 88, 67 };

        var carton = Carton.Crear(BingoId, numeros);

        Assert.NotEqual(Guid.Empty, carton.Id);
        Assert.Equal(BingoId, carton.BingoId);
        Assert.Equal(new List<int> { 3, 12, 45, 67, 68, 71, 80, 85, 88, 90 }, carton.Numeros);
    }

    [Fact]
    public void Crear_ConNumeroRepetidoEntreLosDiez_LanzaNumerosCartonInvalidosException()
    {
        var numeros = new List<int> { 3, 12, 45, 67, 68, 71, 80, 85, 88, 88 };

        Assert.Throws<NumerosCartonInvalidosException>(() => Carton.Crear(BingoId, numeros));
    }

    [Fact]
    public void Crear_ConMenosDeDiezNumeros_LanzaNumerosCartonInvalidosException()
    {
        var numeros = new List<int> { 3, 12, 45, 67, 68, 71, 80, 85, 88 };

        Assert.Throws<NumerosCartonInvalidosException>(() => Carton.Crear(BingoId, numeros));
    }

    [Fact]
    public void Crear_ConUnNumeroFueraDeRango_LanzaNumerosCartonInvalidosException()
    {
        var numeros = new List<int> { 3, 12, 45, 67, 68, 71, 80, 85, 88, 91 };

        Assert.Throws<NumerosCartonInvalidosException>(() => Carton.Crear(BingoId, numeros));
    }

    [Fact]
    public void Crear_ConUnNumeroMenorAUno_LanzaNumerosCartonInvalidosException()
    {
        var numeros = new List<int> { 0, 12, 45, 67, 68, 71, 80, 85, 88, 90 };

        Assert.Throws<NumerosCartonInvalidosException>(() => Carton.Crear(BingoId, numeros));
    }
}
