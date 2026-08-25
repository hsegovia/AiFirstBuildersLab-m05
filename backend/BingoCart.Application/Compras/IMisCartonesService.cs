using BingoCart.Application.Compras.Dtos;

namespace BingoCart.Application.Compras;

/// <summary>
/// Flujo del comprador sobre sus propios cartones adquiridos (spec FEAT-009d, Block 3) — separado
/// de <see cref="ICompraService"/> (confirmación de compra) y de
/// <see cref="ICompraOrganizadorService"/> (flujo del organizador): mitad "cartones" del ticket,
/// expuesta por su propio controller (D-03).
/// </summary>
public interface IMisCartonesService
{
    /// <summary>
    /// Lista paginada de los cartones adquiridos por <paramref name="compradorId"/>, en cualquier
    /// estado de compra (FR-02/AC-02). Clampea <paramref name="pageSize"/> a un máximo de 50
    /// (NFR-01/AC-03) antes de delegar en <see cref="ICompraRepository.ListarCartonesDelCompradorAsync"/>.
    /// </summary>
    Task<MisCartonesResponse> ListarAsync(Guid compradorId, int page, int pageSize);
}
