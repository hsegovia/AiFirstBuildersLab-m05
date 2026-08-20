using System.Globalization;
using BingoCart.Application.Carritos;
using BingoCart.Domain.Carritos;
using StackExchange.Redis;

namespace BingoCart.Infrastructure.Carritos;

/// <summary>
/// Implementa <see cref="ICarritoRepository"/> (Application, Block 1 del spec FEAT-008b) contra
/// Redis vía <see cref="IConnectionMultiplexer"/> — primer uso de Redis del proyecto. Infraestructura
/// pura: no valida que <c>cartonId</c> corresponda a un cartón real ni que su bingo siga activo, eso
/// lo hace Application (Block 2) contra SQL Server antes de invocar este repositorio. Estructura de
/// claves y la elección de un script Lua en vez de MULTI/WATCH están documentadas en la spec
/// ("Decisiones cerradas en PLAN").
/// </summary>
public sealed class CarritoRepository : ICarritoRepository
{
    // Namespacing por dos-puntos (convención estándar de Redis, decisión de PLAN).
    private const string PrefijoCarrito = "carrito:";
    private const string PrefijoReservadoCarton = "reservado:carton:";
    private const string PrefijoDescartados = "descartados:";

    // Reserva atómica en un único round-trip (NFR-01): verifica que la reserva del cartón esté
    // libre o ya pertenezca a esta sesión, la fija/actualiza, y refresca el TTL de TODAS las
    // reservas ya presentes en el carrito al mismo valor (FR-07 — un agregado reinicia el plazo de
    // TODO el carrito, no solo del ítem nuevo). Para el paso 3 el script arma los nombres de las
    // claves de reserva restantes concatenando el prefijo fijo con IDs que el propio Redis ya
    // devolvió vía HKEYS (nunca con `sesionId`), válido en modalidad standalone (documentado como
    // límite: si el proyecto migrara a Redis Cluster, este script necesitaría declarar esas claves
    // vía KEYS[] en vez de construirlas dentro de sí). Mitigación M-01/M-02 del threat model:
    // `sesionId`/`cartonId` llegan EXCLUSIVAMENTE vía ARGV, nunca concatenados al cuerpo del
    // script — evita Lua injection.
    private const string ScriptIntentarAgregar = @"
        local reservadoKey = KEYS[1]
        local carritoKey = KEYS[2]
        local sesionId = ARGV[1]
        local cartonId = ARGV[2]
        local precioUnitario = ARGV[3]
        local ttlSegundos = ARGV[4]

        local propietarioActual = redis.call('GET', reservadoKey)
        if propietarioActual and propietarioActual ~= sesionId then
            return 0
        end

        redis.call('SET', reservadoKey, sesionId)
        redis.call('HSET', carritoKey, cartonId, precioUnitario)
        redis.call('EXPIRE', reservadoKey, ttlSegundos)
        redis.call('EXPIRE', carritoKey, ttlSegundos)

        local cartonIdsEnCarrito = redis.call('HKEYS', carritoKey)
        for _, id in ipairs(cartonIdsEnCarrito) do
            redis.call('EXPIRE', 'reservado:carton:' .. id, ttlSegundos)
        end

        return 1
    ";

    private readonly IConnectionMultiplexer _connectionMultiplexer;

    public CarritoRepository(IConnectionMultiplexer connectionMultiplexer)
    {
        _connectionMultiplexer = connectionMultiplexer;
    }

    public async Task<bool> IntentarAgregarAsync(string sesionId, Guid cartonId, decimal precioUnitario, TimeSpan ttl)
    {
        var db = _connectionMultiplexer.GetDatabase();

        var resultado = await db.ScriptEvaluateAsync(
            ScriptIntentarAgregar,
            new RedisKey[] { ClaveReservado(cartonId), ClaveCarrito(sesionId) },
            new RedisValue[]
            {
                sesionId,
                cartonId.ToString(),
                precioUnitario.ToString(CultureInfo.InvariantCulture),
                (long)ttl.TotalSeconds,
            });

        return (long)resultado == 1;
    }

    public async Task QuitarAsync(string sesionId, Guid cartonId)
    {
        var db = _connectionMultiplexer.GetDatabase();

        // Transacción para atomicidad entre las dos operaciones (HDEL + DEL) — sin ningún EXPIRE
        // (NFR-03): borrar estas dos claves no debe tocar el TTL de ninguna otra.
        var transaccion = db.CreateTransaction();
        _ = transaccion.HashDeleteAsync(ClaveCarrito(sesionId), cartonId.ToString());
        _ = transaccion.KeyDeleteAsync(ClaveReservado(cartonId));
        await transaccion.ExecuteAsync();
    }

    public async Task<IReadOnlyList<ItemCarrito>> ObtenerItemsAsync(string sesionId)
    {
        var db = _connectionMultiplexer.GetDatabase();
        var entradas = await db.HashGetAllAsync(ClaveCarrito(sesionId));

        return entradas
            .Select(e => new ItemCarrito(
                Guid.Parse(e.Name.ToString()),
                decimal.Parse(e.Value.ToString(), CultureInfo.InvariantCulture)))
            .ToList();
    }

    public async Task AgregarDescartadosAsync(string sesionId, IReadOnlyCollection<Guid> cartonIds, TimeSpan ttl)
    {
        if (cartonIds.Count == 0)
        {
            return;
        }

        var db = _connectionMultiplexer.GetDatabase();
        var descartadosKey = ClaveDescartados(sesionId);
        var valores = cartonIds.Select(id => (RedisValue)id.ToString()).ToArray();

        await db.SetAddAsync(descartadosKey, valores);
        // A diferencia del carrito, la clave de descartados SÍ refresca su TTL en cada llamada
        // (decisión de PLAN) — no participa del script Lua de arriba porque no tiene relación con
        // ninguna reserva.
        await db.KeyExpireAsync(descartadosKey, ttl);
    }

    public async Task<IReadOnlySet<Guid>> ObtenerDescartadosAsync(string sesionId)
    {
        var db = _connectionMultiplexer.GetDatabase();
        var miembros = await db.SetMembersAsync(ClaveDescartados(sesionId));

        return miembros.Select(m => Guid.Parse(m.ToString())).ToHashSet();
    }

    private static string ClaveCarrito(string sesionId) => $"{PrefijoCarrito}{sesionId}";

    private static string ClaveReservado(Guid cartonId) => $"{PrefijoReservadoCarton}{cartonId}";

    private static string ClaveDescartados(string sesionId) => $"{PrefijoDescartados}{sesionId}";
}
