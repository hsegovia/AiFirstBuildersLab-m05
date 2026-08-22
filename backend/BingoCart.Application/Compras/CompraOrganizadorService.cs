using BingoCart.Application.Compras.Dtos;
using BingoCart.Domain.Compras;
using BingoCart.Domain.Compras.Exceptions;
using Microsoft.Extensions.Logging;

namespace BingoCart.Application.Compras;

/// <summary>
/// Flujo del organizador sobre sus propias compras (spec FEAT-009c, Block 2) — separado de
/// <see cref="CompraService"/> (flujo del comprador): actores y modelo de autorización distintos.
/// Naming entidad-primero-actor-segundo (<c>Compra</c> + <c>Organizador</c>), a diferencia del único
/// precedente de naming por actor del proyecto (<c>ICompradorIdentityGateway</c>, actor-primero) —
/// deliberado: este tipo vive junto a <see cref="CompraService"/> en <c>Compras/</c>, agrupar por
/// entidad primero es más descubrible en esa carpeta específica.
/// </summary>
public sealed class CompraOrganizadorService : ICompraOrganizadorService
{
    private readonly ICompraRepository _compraRepository;
    private readonly IEnvioMailService _envioMailService;
    private readonly ILogger<CompraOrganizadorService> _logger;

    public CompraOrganizadorService(
        ICompraRepository compraRepository,
        IEnvioMailService envioMailService,
        ILogger<CompraOrganizadorService> logger)
    {
        _compraRepository = compraRepository;
        _envioMailService = envioMailService;
        _logger = logger;
    }

    public async Task ConfirmarPagoAsync(Guid compraId, Guid organizadorId)
    {
        var compra = await ObtenerCompraPropiaAsync(compraId, organizadorId);

        // Domain propaga CompraEstadoInvalidoException sin capturar (FR-02/AC-02).
        compra.ConfirmarPago();

        await _compraRepository.GuardarCambiosAsync();
    }

    public async Task CancelarAsync(Guid compraId, Guid organizadorId)
    {
        var compra = await ObtenerCompraPropiaAsync(compraId, organizadorId);

        // Domain propaga CompraEstadoInvalidoException sin capturar (FR-04/AC-04).
        compra.Cancelar();

        await _compraRepository.GuardarCambiosAsync();

        // Best-effort, recién DESPUÉS de que la cancelación ya está persistida (FR-06): mismo
        // patrón defensivo exacto que CompraService.ConfirmarCompraAsync para
        // EncolarConfirmacionAsync/LiberarCarritoConfirmadoAsync — la cancelación nunca falla porque
        // el encolado de mail falló.
        try
        {
            await _envioMailService.EncolarCancelacionAsync(compra.Id, compra.CompradorId);
        }
        catch (Exception ex)
        {
            // R-01/R-02 (threat model): ÚNICAMENTE tipo de excepción + IDs opacos. NUNCA
            // ex.Message, NUNCA PII del comprador.
            _logger.LogWarning(
                ex,
                "No se pudo encolar el mail de cancelación para la compra {CompraId}.",
                compra.Id);
        }
    }

    public async Task<CompraListadoResponse> ListarPropiasAsync(Guid organizadorId, int page, int pageSize)
    {
        // NFR-01: máximo propio de 50, independiente del clamp de 100 que usa
        // BingoService.ListarPropiosAsync — no es el mismo límite, no se reclama precedente.
        var pageSizeClamped = Math.Min(pageSize, 50);

        var paginadas = await _compraRepository.ListarPorOrganizadorAsync(organizadorId, page, pageSizeClamped);

        var items = paginadas.Items
            .Select(item => new CompraResumenResponse(
                item.Compra.Id,
                item.Compra.Estado.ToString(),
                item.MontoTotal,
                item.Compra.FechaCreacionUtc))
            .ToList();

        var totalPaginas = paginadas.Total == 0
            ? 0
            : (int)Math.Ceiling(paginadas.Total / (double)pageSizeClamped);

        return new CompraListadoResponse(items, paginadas.Total, totalPaginas, page, pageSizeClamped);
    }

    // Chequeo compartido por ConfirmarPagoAsync/CancelarAsync — mirror exacto de
    // BingoService.ObtenerBingoPropioSinComprasAsync: mismo Id inexistente/ajeno →
    // CompraNoEncontradaException (no-enumeración, FR-08/AC-08).
    private async Task<Compra> ObtenerCompraPropiaAsync(Guid compraId, Guid organizadorId)
    {
        var compra = await _compraRepository.ObtenerPorIdAsync(compraId);
        if (compra is null || compra.OrganizadorId != organizadorId)
        {
            throw new CompraNoEncontradaException("La compra indicada no existe.");
        }

        return compra;
    }
}
