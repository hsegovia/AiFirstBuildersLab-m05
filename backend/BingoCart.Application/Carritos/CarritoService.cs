using BingoCart.Application.Bingos;
using BingoCart.Application.Carritos.Dtos;
using BingoCart.Application.Descubrimiento;
using BingoCart.Application.Descubrimiento.Dtos;
using BingoCart.Domain.Bingos;
using BingoCart.Domain.Carritos;
using BingoCart.Domain.Carritos.Exceptions;
using BingoCart.Domain.Organizadores.Exceptions;

namespace BingoCart.Application.Carritos;

/// <summary>
/// Orquesta el carrito de compras (spec FEAT-008b, Block 2) — el único punto que combina Redis
/// (<see cref="ICarritoRepository"/>, Block 1) y SQL Server (<see cref="IBingoRepository"/>,
/// <see cref="IDescubrimientoRepository"/>, ambos de FEAT-003/FEAT-008a). No hace I/O propio: toda
/// persistencia pasa por los repositorios inyectados, mismo patrón que <c>DescubrimientoService</c>/
/// <c>BingoService</c>. <see cref="TimeProvider"/> inyectado — nunca <c>DateTime.UtcNow</c> directo.
/// </summary>
public sealed class CarritoService : ICarritoService
{
    // Valores fijados en PLAN (spec FEAT-008b): reserva de 5 minutos (FR-06/FR-07), descartados de
    // 30 minutos (más largo porque descartar suele ocurrir antes de reservar nada), tanda de 5
    // cartones (mismo tamaño que DescubrimientoService, FEAT-008a).
    private static readonly TimeSpan ReservaTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DescartadosTtl = TimeSpan.FromMinutes(30);
    private const int CantidadPorTanda = 5;

    // Corrección post-SAST (F-SAST-14): límite sobre cartonIdsDescartados ANTES de tocar Redis/SQL.
    // La tanda real nunca supera CantidadPorTanda (5) elementos por request; 50 da 10x de margen
    // para acumular varias tandas descartadas en una sesión sin abrir la puerta a un array de miles
    // que infle sin control el NOT IN (...) de DescubrimientoRepository.ConstruirClausulaExclusion.
    private const int MaxDescartadosPorRequest = 50;

    private readonly ICarritoRepository _carritoRepository;
    private readonly IBingoRepository _bingoRepository;
    private readonly IDescubrimientoRepository _descubrimientoRepository;
    private readonly TimeProvider _timeProvider;

    public CarritoService(
        ICarritoRepository carritoRepository,
        IBingoRepository bingoRepository,
        IDescubrimientoRepository descubrimientoRepository,
        TimeProvider timeProvider)
    {
        _carritoRepository = carritoRepository;
        _bingoRepository = bingoRepository;
        _descubrimientoRepository = descubrimientoRepository;
        _timeProvider = timeProvider;
    }

    public async Task AgregarAsync(string sesionId, Guid cartonId)
    {
        var ahoraUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var cartonInfo = await _bingoRepository.ObtenerParaCarritoAsync(cartonId, ahoraUtc);
        if (cartonInfo is null)
        {
            throw new CartonInexistenteException(
                "El cartón indicado no existe o el bingo al que pertenece ya no está activo.");
        }

        var agregado = await _carritoRepository.IntentarAgregarAsync(
            sesionId, cartonId, cartonInfo.PrecioUnitario, ReservaTtl);
        if (!agregado)
        {
            throw new CartonYaReservadoException("El cartón indicado ya está reservado por otra sesión.");
        }
    }

    public Task QuitarAsync(string sesionId, Guid cartonId) =>
        _carritoRepository.QuitarAsync(sesionId, cartonId);

    public async Task<CarritoResponse> ObtenerCarritoAsync(string sesionId)
    {
        var items = await _carritoRepository.ObtenerItemsAsync(sesionId);
        var ahoraUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var itemsResponse = new List<ItemCarritoResponse>();
        var itemsResueltos = new List<ItemCarrito>();
        foreach (var item in items)
        {
            var cartonInfo = await _bingoRepository.ObtenerParaCarritoAsync(item.CartonId, ahoraUtc);
            if (cartonInfo is null)
            {
                // El bingo pudo vencer mientras el cartón seguía reservado en el carrito (ventana
                // corta pero posible; no cubierto explícitamente por el spec de este bloque): se
                // omite de la vista en vez de romper el GET completo — decisión documentada como
                // asunción en el reporte de implementación.
                continue;
            }

            itemsResueltos.Add(item);
            itemsResponse.Add(new ItemCarritoResponse(
                item.CartonId, cartonInfo.NombreOrganizacion, cartonInfo.NombreEvento, item.PrecioUnitario));
        }

        var carrito = Carrito.DeItems(itemsResueltos);
        return new CarritoResponse(itemsResponse, carrito.CantidadTotal, carrito.MontoTotal);
    }

    public async Task<IReadOnlyList<CartonDescubiertoResponse>> PedirNuevaTandaGlobalAsync(
        string sesionId, IReadOnlyCollection<Guid> descartadosDeEstaTanda)
    {
        ValidarCantidadDescartados(descartadosDeEstaTanda);

        var excluir = await PrepararExclusionAsync(sesionId, descartadosDeEstaTanda);
        var ahoraUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var cartones = await _descubrimientoRepository.ObtenerAleatoriosGlobalAsync(ahoraUtc, CantidadPorTanda, excluir);
        if (cartones.Count == 0)
        {
            return Array.Empty<CartonDescubiertoResponse>();
        }

        var bingoIds = cartones.Select(c => c.BingoId).Distinct().ToList();
        var resumenes = await _descubrimientoRepository.ObtenerResumenBingosAsync(bingoIds);

        return ArmarRespuesta(cartones, resumenes);
    }

    public async Task<IReadOnlyList<CartonDescubiertoResponse>> PedirNuevaTandaPorOrganizadorAsync(
        string sesionId, Guid organizadorId, IReadOnlyCollection<Guid> descartadosDeEstaTanda)
    {
        ValidarCantidadDescartados(descartadosDeEstaTanda);

        // Resolución organizadorId → bingoId activo necesaria porque ObtenerAleatoriosDeBingoAsync
        // (IDescubrimientoRepository) toma un bingoId, no un organizadorId — mismos dos pasos que
        // DescubrimientoService.DescubrirPorOrganizadorAsync (FEAT-008a), reutilizados acá porque el
        // spec de este bloque no detalla ese sub-paso explícitamente (asunción documentada).
        var existeOrganizador = await _descubrimientoRepository.ExisteOrganizadorAsync(organizadorId);
        if (!existeOrganizador)
        {
            throw new OrganizadorNoEncontradoException("El organizador indicado no existe.");
        }

        var excluir = await PrepararExclusionAsync(sesionId, descartadosDeEstaTanda);
        var ahoraUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var bingoId = await _descubrimientoRepository.ObtenerBingoActivoDeOrganizadorAsync(organizadorId, ahoraUtc);
        if (bingoId is null)
        {
            return Array.Empty<CartonDescubiertoResponse>();
        }

        var cartones = await _descubrimientoRepository.ObtenerAleatoriosDeBingoAsync(bingoId.Value, CantidadPorTanda, excluir);
        var resumenes = await _descubrimientoRepository.ObtenerResumenBingosAsync(new[] { bingoId.Value });

        return ArmarRespuesta(cartones, resumenes);
    }

    // Corrección post-SAST (F-SAST-14): corta ANTES de cualquier I/O (Redis o SQL) si el cliente
    // manda más de MaxDescartadosPorRequest elementos — nunca deja que un array desmedido llegue a
    // PrepararExclusionAsync ni a ExisteOrganizadorAsync.
    private static void ValidarCantidadDescartados(IReadOnlyCollection<Guid> descartadosDeEstaTanda)
    {
        if (descartadosDeEstaTanda.Count > MaxDescartadosPorRequest)
        {
            throw new CantidadDescartadosExcedeLimiteException(
                $"No se pueden enviar más de {MaxDescartadosPorRequest} cartones descartados por request.");
        }
    }

    // Descarta lo nuevo, y une lo ya agregado al carrito con todo lo descartado históricamente
    // (FR-03/AC-03) — así "nueva tanda" nunca repite un cartón que el participante ya vio y
    // descartó, o que ya tiene reservado.
    private async Task<IReadOnlyList<Guid>> PrepararExclusionAsync(
        string sesionId, IReadOnlyCollection<Guid> descartadosDeEstaTanda)
    {
        await _carritoRepository.AgregarDescartadosAsync(sesionId, descartadosDeEstaTanda, DescartadosTtl);

        var enCarrito = (await _carritoRepository.ObtenerItemsAsync(sesionId)).Select(i => i.CartonId);
        var descartadosHistoricos = await _carritoRepository.ObtenerDescartadosAsync(sesionId);

        return enCarrito.Union(descartadosHistoricos).ToList();
    }

    // Misma lógica de armado que DescubrimientoService.ArmarRespuesta (FEAT-008a) — no compartida
    // vía código común porque ese método es privado a esa clase y el spec de este bloque no pide
    // extraerlo a un helper reusable (ver hallazgo fuera de alcance en el reporte).
    private static IReadOnlyList<CartonDescubiertoResponse> ArmarRespuesta(
        IReadOnlyList<Carton> cartones, IReadOnlyList<BingoResumen> resumenes)
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
