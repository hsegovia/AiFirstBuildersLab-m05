using BingoCart.Application.Bingos.Dtos;
using BingoCart.Domain.Bingos;
using BingoCart.Domain.Bingos.Exceptions;

namespace BingoCart.Application.Bingos;

/// <summary>
/// Orquesta la creación de un bingo y sus cartones (spec FEAT-003, Block 4). No hace I/O propio:
/// toda persistencia pasa por <see cref="IBingoRepository"/> y toda generación de números por
/// <see cref="ICartonNumberGenerator"/>, ambos inyectados — mismo patrón que
/// <c>OrganizadorService</c>.
/// </summary>
public sealed class BingoService : IBingoService
{
    private readonly IBingoRepository _bingoRepository;
    private readonly ICartonNumberGenerator _cartonNumberGenerator;
    private readonly TimeProvider _timeProvider;

    public BingoService(
        IBingoRepository bingoRepository,
        ICartonNumberGenerator cartonNumberGenerator,
        TimeProvider timeProvider)
    {
        _bingoRepository = bingoRepository;
        _cartonNumberGenerator = cartonNumberGenerator;
        _timeProvider = timeProvider;
    }

    public async Task<BingoCreadoResponse> CrearAsync(CrearBingoRequest request, Guid organizadorId)
    {
        var ahoraUtc = _timeProvider.GetUtcNow().UtcDateTime;

        // (1) Domain primero: gratis, sin I/O — fail-fast antes de tocar la base de datos. Puede
        // lanzar FechaSorteoInvalidaException/CantidadCartonesExcedeLimiteException/
        // CantidadCartonesInvalidaException/CostoPorCartonInvalidoException.
        var bingo = Bingo.Crear(
            request.NombreEvento,
            request.FechaSorteoUtc,
            request.CantidadCartones,
            request.CostoPorCarton,
            organizadorId,
            ahoraUtc);

        // (2) I/O barato, pero todavía antes del paso costoso (generación de cartones) — sigue
        // satisfaciendo la mitigación de threat modeling de no generar cartones si el organizador
        // ya tiene un bingo activo (FR-06, TM-01).
        var tieneBingoActivo = await _bingoRepository.TieneBingoActivoAsync(organizadorId, ahoraUtc);
        if (tieneBingoActivo)
        {
            throw new BingoActivoExistenteException("El organizador ya tiene un bingo activo.");
        }

        // (3) Generación 100% en memoria (NFR-01).
        var conjuntos = _cartonNumberGenerator.GenerarConjuntosUnicos(request.CantidadCartones);

        // (4) Cada Carton se construye a partir del agregado ya validado por Domain.
        var cartones = conjuntos
            .Select(conjunto => Carton.Crear(bingo.Id, conjunto))
            .ToList();

        // (5) Persistencia atómica de bingo + cartones.
        await _bingoRepository.CrearAsync(bingo, cartones);

        // (6) Confirmación de creación, sin los cartones individuales (ver BingoCreadoResponse).
        return new BingoCreadoResponse(
            bingo.Id,
            bingo.NombreEvento,
            bingo.FechaSorteoUtc,
            bingo.CantidadCartones,
            bingo.CostoPorCarton);
    }

    public async Task<BingoListadoResponse> ListarPropiosAsync(Guid organizadorId, int page, int pageSize)
    {
        // Defensa en profundidad ante R-02 del threat model: [Range] en ListarBingosQuery valida el
        // mínimo, no el máximo — acá se clampea el máximo real aplicado, sin depender únicamente de
        // la validación de modelo.
        var pageSizeClamped = Math.Min(pageSize, 100);

        var paginados = await _bingoRepository.ListarPorOrganizadorAsync(organizadorId, page, pageSizeClamped);

        var items = paginados.Bingos
            .Select(b => new BingoCreadoResponse(
                b.Id,
                b.NombreEvento,
                b.FechaSorteoUtc,
                b.CantidadCartones,
                b.CostoPorCarton))
            .ToList();

        var totalPaginas = paginados.Total == 0
            ? 0
            : (int)Math.Ceiling(paginados.Total / (double)pageSizeClamped);

        return new BingoListadoResponse(items, paginados.Total, totalPaginas, page, pageSizeClamped);
    }
}
