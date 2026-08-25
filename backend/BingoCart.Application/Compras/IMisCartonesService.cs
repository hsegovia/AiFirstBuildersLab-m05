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

    /// <summary>
    /// Genera el PDF del cartón <paramref name="cartonId"/> del comprador autenticado (spec
    /// FEAT-009d, Block 4, FR-04/AC-04). Reutiliza <see cref="ICartonPdfRenderer"/> tal cual (Block
    /// 2) — el PDF se genera en memoria y no se persiste (mitigación R-03). Lanza
    /// <c>CartonNoEncontradoException</c> (Domain) con el mismo mensaje tanto si el cartón no existe
    /// como si pertenece a otro comprador (anti-enumeración, R-01).
    /// </summary>
    Task<byte[]> ObtenerPdfAsync(Guid compradorId, Guid cartonId);
}
