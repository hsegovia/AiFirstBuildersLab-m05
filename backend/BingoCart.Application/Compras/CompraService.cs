using BingoCart.Application.Bingos;
using BingoCart.Application.Carritos;
using BingoCart.Application.Compras.Dtos;
using BingoCart.Domain.Compras;
using BingoCart.Domain.Compras.Exceptions;
using Microsoft.Extensions.Logging;

namespace BingoCart.Application.Compras;

/// <summary>
/// Único punto que combina Redis (<see cref="ICarritoRepository"/>), SQL de bingos
/// (<see cref="IBingoRepository"/>) y SQL de compras (<see cref="ICompraRepository"/>) — ninguno de
/// los tres se conoce entre sí (spec FEAT-009a, Block 2). Aplica el orden CHECK→COMMIT→RELEASE
/// cerrado en PLAN: revalida las reservas de Redis ANTES de persistir en SQL, y solo libera el
/// carrito de Redis DESPUÉS de un commit SQL exitoso — nunca al revés, y nunca si el commit falla.
/// </summary>
public sealed class CompraService : ICompraService
{
    private readonly ICarritoRepository _carritoRepository;
    private readonly IBingoRepository _bingoRepository;
    private readonly ICompraRepository _compraRepository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CompraService> _logger;

    public CompraService(
        ICarritoRepository carritoRepository,
        IBingoRepository bingoRepository,
        ICompraRepository compraRepository,
        TimeProvider timeProvider,
        ILogger<CompraService> logger)
    {
        _carritoRepository = carritoRepository;
        _bingoRepository = bingoRepository;
        _compraRepository = compraRepository;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ConfirmarCompraResponse> ConfirmarCompraAsync(string sesionId, Guid compradorId, MedioPago medioPago)
    {
        // (1) Carrito vacío: ni siquiera vale la pena revalidar contra Redis/SQL (AC-02).
        var itemsCarrito = await _carritoRepository.ObtenerItemsAsync(sesionId);
        if (itemsCarrito.Count == 0)
        {
            throw new CarritoVacioException("El carrito está vacío: no hay nada para confirmar.");
        }

        // (2) CHECK: revalidación atómica y de solo lectura contra Redis (AC-07/AC-08). Si alguna
        // reserva ya no es válida, se corta acá sin tocar SQL.
        var revalidacion = await _carritoRepository.RevalidarReservasAsync(sesionId);
        if (!revalidacion.EsValido)
        {
            throw new ReservaCarritoInvalidaException(
                revalidacion.CartonIdsInvalidos,
                "Una o más reservas del carrito ya no son válidas.");
        }

        var cartonIds = revalidacion.Items.Select(item => item.CartonId).ToList();
        var datosCartones = await _bingoRepository.ObtenerParaConfirmarCompraAsync(cartonIds);
        var datosPorCartonId = datosCartones.ToDictionary(dato => dato.CartonId);

        // Caso de borde aceptado en PLAN: el bingo de un cartón ya reservado en Redis fue eliminado
        // entre agregarlo al carrito y confirmar. Se trata igual que una reserva inválida.
        var cartonIdsSinCorrespondencia = cartonIds.Where(id => !datosPorCartonId.ContainsKey(id)).ToList();
        if (cartonIdsSinCorrespondencia.Count > 0)
        {
            throw new ReservaCarritoInvalidaException(
                cartonIdsSinCorrespondencia,
                "Uno o más cartones del carrito ya no existen.");
        }

        var ahoraUtc = _timeProvider.GetUtcNow().UtcDateTime;

        // (3)/(4) Agrupa por organizador: una Compra independiente por cada uno (AC-03).
        var compras = new List<Compra>();
        var comprasCreadas = new List<CompraCreada>();
        foreach (var grupo in revalidacion.Items.GroupBy(item => datosPorCartonId[item.CartonId].OrganizadorId))
        {
            var itemsCompra = grupo
                .Select(item => new ItemCompra(item.CartonId, item.PrecioUnitario))
                .ToList();

            var compra = Compra.Crear(grupo.Key, compradorId, itemsCompra, medioPago, ahoraUtc);
            compras.Add(compra);

            var nombreOrganizacion = datosPorCartonId[grupo.First().CartonId].NombreOrganizacion;
            comprasCreadas.Add(new CompraCreada(
                compra.Id,
                compra.OrganizadorId,
                nombreOrganizacion,
                itemsCompra.Count,
                itemsCompra.Sum(item => item.PrecioUnitario)));
        }

        // (5) COMMIT: "todo o nada" real en una única transacción EF Core (Infrastructure). Una
        // violación del UNIQUE de CompraCartones.CartonId (carrera perdida contra otra confirmación,
        // caso extremadamente raro dado que Redis ya serializa la reserva) se traduce a
        // ReservaCarritoInvalidaException dentro de CompraRepository, no acá — Application no conoce
        // EF Core (corrective round 2, spec FEAT-009a Block 2). Se deja propagar sin capturar.
        await _compraRepository.CrearVariasAsync(compras);

        // (6) RELEASE: solo después del commit SQL exitoso. Si falla, la compra ya quedó persistida
        // igual — se loguea como warning (nunca se relanza) y el TTL de 5 minutos ya existente limpia
        // Redis solo (decisión de PLAN). Se captura Exception a propósito: Application no conoce el
        // tipo concreto que pueda lanzar la implementación de Redis de este puerto.
        try
        {
            await _carritoRepository.LiberarCarritoConfirmadoAsync(sesionId, cartonIds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "No se pudo liberar el carrito de la sesión {SesionId} tras confirmar la compra. La compra ya está persistida.",
                sesionId);
        }

        // (7)
        return new ConfirmarCompraResponse(comprasCreadas);
    }
}
