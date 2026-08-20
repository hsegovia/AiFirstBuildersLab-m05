namespace BingoCart.Infrastructure.Data;

/// <summary>
/// Modelo de persistencia pura (Infrastructure, NUNCA expuesto en la API) para la tabla
/// `CompraCartones` (spec FEAT-009a, Block 1) — deliberadamente distinto de
/// <c>BingoCart.Domain.Compras.ItemCompra</c> (el `record` inmutable de dominio): EF Core necesita
/// una clase mutable con setters para materializar filas, y esta tabla vive fuera del grafo de
/// navegación de <c>Compra</c> (ver <c>AppDbContext.OnModelCreating</c>, `Ignore(c => c.Items)`).
/// `CartonId` es la clave primaria: un cartón nunca puede pertenecer a más de una compra en toda la
/// vida del sistema (NFR-01/RNF-03) — la última línea de defensa contra la doble venta, independiente
/// de la revalidación de Redis.
/// </summary>
public class CompraCarton
{
    public Guid CompraId { get; set; }

    public Guid CartonId { get; set; }

    public decimal PrecioUnitario { get; set; }
}
