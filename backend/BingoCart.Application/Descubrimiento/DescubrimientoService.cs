using BingoCart.Application.Descubrimiento.Dtos;
using BingoCart.Domain.Bingos;
using BingoCart.Domain.Organizadores.Exceptions;

namespace BingoCart.Application.Descubrimiento;

/// <summary>
/// Orquesta el descubrimiento público de cartones (spec FEAT-008a, Block 2). No hace I/O propio:
/// toda persistencia pasa por <see cref="IDescubrimientoRepository"/>, inyectado — mismo patrón que
/// <c>BingoService</c>/<c>OrganizadorService</c>.
/// </summary>
public sealed class DescubrimientoService : IDescubrimientoService
{
    // Tamaño de tanda de ambos métodos (FR-01/FR-05): ni la spec ni el PRD lo hacen configurable.
    private const int CantidadPorTanda = 5;

    private readonly IDescubrimientoRepository _repository;
    private readonly TimeProvider _timeProvider;

    public DescubrimientoService(IDescubrimientoRepository repository, TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<CartonDescubiertoResponse>> DescubrirGlobalAsync()
    {
        var ahoraUtc = _timeProvider.GetUtcNow().UtcDateTime;

        // Array.Empty<Guid>() (spec FEAT-008b, Block 2): este método no necesita excluir nada — el
        // parámetro nuevo es para "nueva tanda" del carrito, sin cambio de comportamiento acá.
        var cartones = await _repository.ObtenerAleatoriosGlobalAsync(ahoraUtc, CantidadPorTanda, Array.Empty<Guid>());
        if (cartones.Count == 0)
        {
            // Sin cartones elegibles: no tiene sentido resolver resúmenes de bingos que no van a
            // usarse (AC-09) — una consulta menos.
            return Array.Empty<CartonDescubiertoResponse>();
        }

        var bingoIds = cartones.Select(c => c.BingoId).Distinct().ToList();
        var resumenes = await _repository.ObtenerResumenBingosAsync(bingoIds);

        return ArmarRespuesta(cartones, resumenes);
    }

    public async Task<IReadOnlyList<CartonDescubiertoResponse>> DescubrirPorOrganizadorAsync(Guid organizadorId)
    {
        var existeOrganizador = await _repository.ExisteOrganizadorAsync(organizadorId);
        if (!existeOrganizador)
        {
            throw new OrganizadorNoEncontradoException("El organizador indicado no existe.");
        }

        var ahoraUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var bingoId = await _repository.ObtenerBingoActivoDeOrganizadorAsync(organizadorId, ahoraUtc);
        if (bingoId is null)
        {
            // Organizador existente pero sin bingo vigente (AC-04/AC-07): no tiene sentido pedir
            // cartones de un bingo que no existe — una consulta menos.
            return Array.Empty<CartonDescubiertoResponse>();
        }

        var cartones = await _repository.ObtenerAleatoriosDeBingoAsync(bingoId.Value, CantidadPorTanda, Array.Empty<Guid>());

        // Siempre 0 o 1 elemento: un único bingoId ya conocido, no una lista paginada.
        var resumenes = await _repository.ObtenerResumenBingosAsync(new[] { bingoId.Value });

        return ArmarRespuesta(cartones, resumenes);
    }

    private static IReadOnlyList<CartonDescubiertoResponse> ArmarRespuesta(
        IReadOnlyList<Carton> cartones,
        IReadOnlyList<BingoResumen> resumenes)
    {
        var resumenesPorBingoId = resumenes.ToDictionary(r => r.Id);

        return cartones
            .Select(carton =>
            {
                var resumen = resumenesPorBingoId[carton.BingoId];
                return new CartonDescubiertoResponse(
                    carton.Id,
                    resumen.NombreOrganizacion,
                    resumen.NombreEvento,
                    resumen.FechaSorteoUtc,
                    resumen.CostoPorCarton,
                    carton.Numeros);
            })
            .ToList();
    }
}
