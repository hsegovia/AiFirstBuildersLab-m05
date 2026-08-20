using BingoCart.Domain.Compradores;
using BingoCart.Domain.Compradores.Exceptions;

namespace BingoCart.Domain.Tests.Compradores;

/// <summary>
/// Tests de <see cref="Comprador"/> — espejo de <c>OrganizadorTests</c> (spec FEAT-009a, Block 1):
/// reutiliza el mismo <c>CuitValidator</c> de <c>Domain.Organizadores</c>, solo el CUIT se valida en
/// <see cref="Comprador.Crear"/> (unicidad de mail y política de password son responsabilidad de
/// Application/Identity, no de este agregado).
/// </summary>
public class CompradorTests
{
    // Mismo CUIT verificado a mano que OrganizadorTests (algoritmo estándar CUIT/CUIL).
    private const string CuitValido = "30500010912";

    [Fact]
    public void Crear_ConCuitInvalido_LanzaCuitInvalidoException()
    {
        var ex = Assert.Throws<CuitInvalidoException>(() =>
            Comprador.Crear("Pérez", "Juan", "12345", "juan.perez@example.com"));

        Assert.DoesNotContain("12345", ex.Message);
    }

    [Fact]
    public void Crear_ConCuitValido_ConstruyeLaEntidadCorrectamente()
    {
        var comprador = Comprador.Crear("Pérez", "Juan", CuitValido, "juan.perez@example.com");

        Assert.NotEqual(Guid.Empty, comprador.Id);
        Assert.Equal("Pérez", comprador.Apellido);
        Assert.Equal("Juan", comprador.Nombre);
        Assert.Equal(CuitValido, comprador.Cuit);
        Assert.Equal("juan.perez@example.com", comprador.Mail);
    }
}
