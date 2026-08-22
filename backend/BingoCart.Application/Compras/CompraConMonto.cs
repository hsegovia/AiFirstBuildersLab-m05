using BingoCart.Domain.Compras;

namespace BingoCart.Application.Compras;

/// <summary>
/// Empareja una <see cref="Compra"/> con su monto total ya calculado (spec FEAT-009c, Block 2).
/// Necesario porque <c>MontoTotal</c> no es derivable directamente de <see cref="Compra"/>:
/// <c>AppDbContext</c> mapea <c>entity.Ignore(c =&gt; c.Items)</c> (el precio real vive únicamente
/// en <c>CompraCartones.PrecioUnitario</c>, fuera del grafo de navegación a propósito, mismo
/// criterio ya documentado en <c>CompraCarton.cs</c>). Infrastructure (Block 3) calcula
/// <see cref="MontoTotal"/> vía <c>Sum</c> agrupado por <c>CompraId</c> — agregación SQL, nunca un
/// cálculo de negocio en el repositorio.
/// </summary>
public sealed record CompraConMonto(Compra Compra, decimal MontoTotal);
