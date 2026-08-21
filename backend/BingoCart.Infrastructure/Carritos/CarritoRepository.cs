using System.Globalization;
using BingoCart.Application.Carritos;
using BingoCart.Application.Carritos.Dtos;
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

    // Revalidación de SOLO LECTURA (spec FEAT-009a, Block 1, decisión de PLAN "CHECK y COMMIT son
    // dos operaciones separadas") — nunca ejecuta SET/DEL/EXPIRE, solo GET sobre cada
    // `reservado:carton:{cartonId}` del hash `carrito:{sesionId}`. Devuelve dos listas paralelas
    // (cartonIds válidos con su precio, cartonIds inválidos) para que el llamador (.NET) arme el DTO
    // sin una segunda llamada a Redis. Mismo criterio de seguridad que ScriptIntentarAgregar:
    // `sesionId` viaja EXCLUSIVAMENTE vía ARGV, nunca concatenado al cuerpo del script.
    private const string ScriptRevalidar = @"
        local carritoKey = KEYS[1]
        local sesionId = ARGV[1]

        local entradas = redis.call('HGETALL', carritoKey)
        local validos = {}
        local invalidos = {}

        for i = 1, #entradas, 2 do
            local cartonId = entradas[i]
            local precioUnitario = entradas[i + 1]
            local propietarioActual = redis.call('GET', 'reservado:carton:' .. cartonId)

            if propietarioActual == sesionId then
                table.insert(validos, cartonId)
                table.insert(validos, precioUnitario)
            else
                table.insert(invalidos, cartonId)
            end
        end

        return { validos, invalidos }
    ";

    // Libera un carrito ya confirmado (spec FEAT-009a, Block 1) — SOLO se invoca después de que la
    // transacción SQL de la compra commiteó exitosamente (decisión de PLAN, orden CHECK→COMMIT→
    // RELEASE). `cartonIds` llega vía ARGV como lista variable (ARGV[1] en adelante), nunca
    // concatenado al cuerpo del script.
    private const string ScriptLiberar = @"
        local carritoKey = KEYS[1]
        redis.call('DEL', carritoKey)

        for i = 1, #ARGV do
            redis.call('DEL', 'reservado:carton:' .. ARGV[i])
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

    public async Task<RevalidacionCarrito> RevalidarReservasAsync(string sesionId)
    {
        var db = _connectionMultiplexer.GetDatabase();

        var resultado = await db.ScriptEvaluateAsync(
            ScriptRevalidar,
            new RedisKey[] { ClaveCarrito(sesionId) },
            new RedisValue[] { sesionId });

        // `!` seguro en todo este método: `ScriptRevalidar` siempre devuelve la estructura fija de
        // dos elementos `{ validos, invalidos }`, y cada elemento dentro de esos arreglos es en sí
        // mismo un string de Redis (nunca `nil`) — el script sólo incluye un cartónId en `validos`
        // o `invalidos` cuando encontró la clave correspondiente con `HGETALL`/`GET` y devolvió su
        // valor; no hay camino en el script donde devuelva `nil` para un elemento presente en
        // alguno de los dos arreglos.
        var arreglos = (RedisResult[])resultado!;
        var validos = (RedisResult[])arreglos[0]!;
        var invalidos = (RedisResult[])arreglos[1]!;

        var cartonIdsInvalidos = invalidos
            .Select(v => Guid.Parse((string)v!))
            .ToList();

        if (cartonIdsInvalidos.Count > 0)
        {
            return new RevalidacionCarrito(false, Array.Empty<ItemCarrito>(), cartonIdsInvalidos);
        }

        var items = new List<ItemCarrito>();
        for (var i = 0; i < validos.Length; i += 2)
        {
            var cartonId = Guid.Parse((string)validos[i]!);
            var precioUnitario = decimal.Parse((string)validos[i + 1]!, CultureInfo.InvariantCulture);
            items.Add(new ItemCarrito(cartonId, precioUnitario));
        }

        return new RevalidacionCarrito(true, items, Array.Empty<Guid>());
    }

    public async Task LiberarCarritoConfirmadoAsync(string sesionId, IReadOnlyCollection<Guid> cartonIds)
    {
        var db = _connectionMultiplexer.GetDatabase();

        var argumentos = cartonIds.Select(id => (RedisValue)id.ToString()).ToArray();

        await db.ScriptEvaluateAsync(
            ScriptLiberar,
            new RedisKey[] { ClaveCarrito(sesionId) },
            argumentos);
    }

    private static string ClaveCarrito(string sesionId) => $"{PrefijoCarrito}{sesionId}";

    private static string ClaveReservado(Guid cartonId) => $"{PrefijoReservadoCarton}{cartonId}";

    private static string ClaveDescartados(string sesionId) => $"{PrefijoDescartados}{sesionId}";
}
