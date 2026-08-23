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

        var carton = Carton.Crear(BingoId, numeros, numeroCorrelativo: 1);

        Assert.NotEqual(Guid.Empty, carton.Id);
        Assert.Equal(BingoId, carton.BingoId);
        Assert.Equal(new List<int> { 3, 12, 45, 67, 68, 71, 80, 85, 88, 90 }, carton.Numeros);
    }

    [Fact]
    public void Crear_ConNumeroRepetidoEntreLosDiez_LanzaNumerosCartonInvalidosException()
    {
        var numeros = new List<int> { 3, 12, 45, 67, 68, 71, 80, 85, 88, 88 };

        Assert.Throws<NumerosCartonInvalidosException>(() => Carton.Crear(BingoId, numeros, numeroCorrelativo: 1));
    }

    [Fact]
    public void Crear_ConMenosDeDiezNumeros_LanzaNumerosCartonInvalidosException()
    {
        var numeros = new List<int> { 3, 12, 45, 67, 68, 71, 80, 85, 88 };

        Assert.Throws<NumerosCartonInvalidosException>(() => Carton.Crear(BingoId, numeros, numeroCorrelativo: 1));
    }

    [Fact]
    public void Crear_ConUnNumeroFueraDeRango_LanzaNumerosCartonInvalidosException()
    {
        var numeros = new List<int> { 3, 12, 45, 67, 68, 71, 80, 85, 88, 91 };

        Assert.Throws<NumerosCartonInvalidosException>(() => Carton.Crear(BingoId, numeros, numeroCorrelativo: 1));
    }

    [Fact]
    public void Crear_ConUnNumeroMenorAUno_LanzaNumerosCartonInvalidosException()
    {
        var numeros = new List<int> { 0, 12, 45, 67, 68, 71, 80, 85, 88, 90 };

        Assert.Throws<NumerosCartonInvalidosException>(() => Carton.Crear(BingoId, numeros, numeroCorrelativo: 1));
    }

    [Fact]
    public void Crear_AsignaElNumeroCorrelativoRecibido()
    {
        var numeros = new List<int> { 3, 12, 45, 67, 68, 71, 80, 85, 88, 90 };

        var carton = Carton.Crear(BingoId, numeros, numeroCorrelativo: 7);

        Assert.Equal(7, carton.NumeroCorrelativo);
    }

    [Fact]
    public void Crear_ConCorrelativoCero_LanzaNumeroCorrelativoInvalidoException()
    {
        var numeros = new List<int> { 3, 12, 45, 67, 68, 71, 80, 85, 88, 90 };

        Assert.Throws<NumeroCorrelativoInvalidoException>(
            () => Carton.Crear(BingoId, numeros, numeroCorrelativo: 0));
    }

    [Fact]
    public void Crear_ConCorrelativoNegativo_LanzaNumeroCorrelativoInvalidoException()
    {
        var numeros = new List<int> { 3, 12, 45, 67, 68, 71, 80, 85, 88, 90 };

        Assert.Throws<NumeroCorrelativoInvalidoException>(
            () => Carton.Crear(BingoId, numeros, numeroCorrelativo: -1));
    }
}
