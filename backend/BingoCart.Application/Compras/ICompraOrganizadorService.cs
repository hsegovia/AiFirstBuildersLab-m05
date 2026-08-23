using BingoCart.Application.Compras.Dtos;

namespace BingoCart.Application.Compras;

/// <summary>
/// Orquesta el flujo del organizador sobre sus propias compras (spec FEAT-009c, Block 2):
/// confirmación/cancelación manual de pago y listado mínimo. Separado de <see cref="ICompraService"/>
/// (ese es el flujo del comprador) — actores y modelo de autorización distintos.
/// </summary>
public interface ICompraOrganizadorService
{
    /// <summary>
    /// Confirma el pago de la compra <paramref name="compraId"/>, siempre que pertenezca a
    /// <paramref name="organizadorId"/> (FR-01/AC-01). Devuelve el resumen ya actualizado (API
    /// contract de Block 3: 200 con <see cref="CompraResumenResponse"/>).
    /// </summary>
    /// <exception cref="Domain.Compras.Exceptions.CompraNoEncontradaException">
    /// La compra no existe o es de otro organizador (FR-08/AC-08).
    /// </exception>
    /// <exception cref="Domain.Compras.Exceptions.CompraEstadoInvalidoException">
    /// La compra no está pendiente de confirmación de pago (FR-02/AC-02).
    /// </exception>
    Task<CompraResumenResponse> ConfirmarPagoAsync(Guid compraId, Guid organizadorId);

    /// <summary>
    /// Cancela la compra <paramref name="compraId"/>, siempre que pertenezca a
    /// <paramref name="organizadorId"/> (FR-03/AC-03, FR-04/AC-04). Encola el mail de cancelación
    /// best-effort (FR-06): una falla al encolar nunca revierte la cancelación ya persistida.
    /// Devuelve el resumen ya actualizado (API contract de Block 3: 200 con
    /// <see cref="CompraResumenResponse"/>).
    /// </summary>
    /// <exception cref="Domain.Compras.Exceptions.CompraNoEncontradaException">
    /// La compra no existe o es de otro organizador (FR-08/AC-08).
    /// </exception>
    /// <exception cref="Domain.Compras.Exceptions.CompraEstadoInvalidoException">
    /// La compra no está pendiente de confirmación de pago (FR-04/AC-04).
    /// </exception>
    Task<CompraResumenResponse> CancelarAsync(Guid compraId, Guid organizadorId);

    /// <summary>
    /// Devuelve la página <paramref name="page"/> de compras propias de <paramref name="organizadorId"/>
    /// (FR-07/AC-07), sin datos del comprador. <paramref name="pageSize"/> se clampea a un máximo de
    /// 50 (NFR-01).
    /// </summary>
    Task<CompraListadoResponse> ListarPropiasAsync(Guid organizadorId, int page, int pageSize);
}
