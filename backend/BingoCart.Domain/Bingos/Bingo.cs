using BingoCart.Domain.Bingos.Exceptions;

namespace BingoCart.Domain.Bingos;

/// <summary>
/// Agregado de dominio: un bingo creado por un organizador (FEAT-003). Inmutable tras su creación
/// — no expone setters públicos; la única forma de construirlo es <see cref="Crear"/>, que aplica
/// las invariantes de fecha de sorteo, cantidad de cartones y costo por cartón (FR-01, FR-02,
/// FR-04, FR-07). Este factory no genera cartones ni conoce el generador CSPRNG — eso es
/// responsabilidad de Block 2/4.
/// </summary>
public sealed class Bingo
{
    public Guid Id { get; private init; }

    public Guid OrganizadorId { get; private init; }

    public string NombreEvento { get; private set; } = string.Empty;

    public DateTime FechaSorteoUtc { get; private set; }

    public DateTime FechaCreacionUtc { get; private init; }

    public int CantidadCartones { get; private init; }

    public decimal CostoPorCarton { get; private set; }

    private Bingo()
    {
    }

    /// <summary>
    /// Crea un <see cref="Bingo"/> validando fecha de sorteo, cantidad de cartones y costo por
    /// cartón, en ese orden. <c>NombreEvento</c> NO se valida acá — esa validación vive en el DTO
    /// de Application (Block 4), mismo criterio que <c>Organizador.Crear</c> no valida
    /// <c>NombreOrganizacion</c>. <paramref name="ahoraUtc"/> se recibe como parámetro explícito
    /// (no se consulta ningún reloj) para mantener el dominio puro/sin I/O.
    /// </summary>
    /// <exception cref="FechaSorteoInvalidaException">
    /// La fecha de sorteo no es posterior a <paramref name="ahoraUtc"/>.
    /// </exception>
    /// <exception cref="CantidadCartonesExcedeLimiteException">
    /// La cantidad de cartones solicitada supera el límite de 5.000 (RF-03).
    /// </exception>
    /// <exception cref="CantidadCartonesInvalidaException">
    /// La cantidad de cartones solicitada no es mayor a cero.
    /// </exception>
    /// <exception cref="CostoPorCartonInvalidoException">
    /// El costo por cartón no es mayor a cero.
    /// </exception>
    public static Bingo Crear(
        string nombreEvento,
        DateTime fechaSorteoUtc,
        int cantidadCartones,
        decimal costoPorCarton,
        Guid organizadorId,
        DateTime ahoraUtc)
    {
        if (fechaSorteoUtc <= ahoraUtc)
        {
            throw new FechaSorteoInvalidaException("La fecha de sorteo debe ser futura.");
        }

        if (cantidadCartones > 5000)
        {
            throw new CantidadCartonesExcedeLimiteException(
                "La cantidad de cartones no puede superar el límite de 5.000 por bingo.");
        }

        if (cantidadCartones <= 0)
        {
            throw new CantidadCartonesInvalidaException("La cantidad de cartones debe ser mayor a cero.");
        }

        if (costoPorCarton <= 0)
        {
            throw new CostoPorCartonInvalidoException("El costo por cartón debe ser mayor a cero.");
        }

        return new Bingo
        {
            Id = Guid.NewGuid(),
            OrganizadorId = organizadorId,
            NombreEvento = nombreEvento,
            FechaSorteoUtc = fechaSorteoUtc,
            FechaCreacionUtc = ahoraUtc,
            CantidadCartones = cantidadCartones,
            CostoPorCarton = costoPorCarton,
        };
    }

    /// <summary>
    /// Actualiza <see cref="NombreEvento"/>, <see cref="FechaSorteoUtc"/> y
    /// <see cref="CostoPorCarton"/> reaplicando las mismas invariantes que <see cref="Crear"/> (fecha
    /// futura, costo mayor a cero, en ese orden). <see cref="Id"/>, <see cref="OrganizadorId"/> y
    /// <see cref="CantidadCartones"/> nunca cambian — fuera de alcance del PRD. Muta la instancia en
    /// lugar de devolver una nueva para que EF Core, si esta instancia fue cargada trackeada (vía
    /// <c>IBingoRepository.ObtenerPorIdAsync</c>), detecte el cambio automáticamente al llamar
    /// <c>SaveChangesAsync</c>. <c>NombreEvento</c> NO se valida acá, mismo criterio que
    /// <see cref="Crear"/> (la validación de forma vive en el DTO de Application).
    /// </summary>
    /// <exception cref="FechaSorteoInvalidaException">
    /// La fecha de sorteo no es posterior a <paramref name="ahoraUtc"/>.
    /// </exception>
    /// <exception cref="CostoPorCartonInvalidoException">
    /// El costo por cartón no es mayor a cero.
    /// </exception>
    public void Actualizar(string nombreEvento, DateTime fechaSorteoUtc, decimal costoPorCarton, DateTime ahoraUtc)
    {
        if (fechaSorteoUtc <= ahoraUtc)
        {
            throw new FechaSorteoInvalidaException("La fecha de sorteo debe ser futura.");
        }

        if (costoPorCarton <= 0)
        {
            throw new CostoPorCartonInvalidoException("El costo por cartón debe ser mayor a cero.");
        }

        NombreEvento = nombreEvento;
        FechaSorteoUtc = fechaSorteoUtc;
        CostoPorCarton = costoPorCarton;
    }
}
