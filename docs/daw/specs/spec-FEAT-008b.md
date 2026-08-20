# Spec FEAT-008b: Carrito de compras

| Field | Value |
|-------|-------|
| Ticket | FEAT-008b |
| PRD | docs/daw/prd/prd-FEAT-008b.md |
| Tier | FEATURE |
| Date | 2026-08-20 |
| Spec loops | 0 |

## Summary

Implementa un carrito de compras por sesión anónima (sin registro ni login): agregar cartones de
una tanda de descubrimiento (FEAT-008a) al carrito, descartar la tanda actual y pedir una nueva sin
repetir cartones ya agregados/descartados, ver el carrito acumulado con total y monto, quitar
cartones individuales, y una reserva de 5 minutos sobre todo el carrito que se reinicia con cada
agregado y libera automáticamente al vencer. Primer uso de Redis en el proyecto (declarado en el
stack, sin código hasta ahora, confirmado por impact scan) y primera pieza de estado por sesión sin
autenticación. **Backend-only**, sin pantalla de carrito en el frontend todavía.

## Coverage: PRD → blocks

| Requirement | Covered by |
|---|---|
| FR-01 | Block 2, Block 3 |
| FR-02 | Block 3 |
| FR-03 | Block 1, Block 2, Block 3 |
| FR-04 | Block 1, Block 2, Block 3 |
| FR-05 | Block 1, Block 2, Block 3 |
| FR-06 | Block 1, Block 2, Block 3 |
| FR-07 | Block 1, Block 2 |
| FR-08 | Block 1 (TTL de Redis) |
| FR-09 | Block 1, Block 2 |
| FR-10 | Block 1, Block 2, Block 3 |
| NFR-01 | Strategy: reserva atómica vía script Lua (`EVAL`) que verifica y reserva en una sola operación (Block 1), con test de concurrencia real (dos sesiones, un cartón). |
| NFR-02 | Strategy: política de rate limiting nueva `"carrito"` (Block 3), 60 req/5min por IP, mismo mecanismo `FixedWindowLimiter` ya usado por `"descubrimiento"` (FEAT-008a). |
| NFR-03 | Strategy: quitar un ítem del carrito nunca ejecuta `EXPIRE` sobre la clave del carrito ni sobre las claves de reserva restantes (Block 1) — Redis no toca el TTL de una clave por un `HDEL`/`DEL` de otra. |

## Dependencies between blocks

Block 1 (Domain + Infraestructura Redis) no depende de nada nuevo — es la primera vez que Redis
entra al proyecto, sin precedente que reutilizar. Block 2 (extensión de descubrimiento con exclusión
+ `CarritoService`) depende de Block 1 (usa `ICarritoRepository`) y modifica código de FEAT-008a ya
en `main` (`IDescubrimientoRepository`/`DescubrimientoRepository`/`DescubrimientoService`). Block 3
(Api) depende de Block 2. Orden: 1 → 2 → 3.

**Decisiones cerradas en PLAN (no reabrir en CODE):**

- **Todo el estado del carrito vive en Redis, no en SQL Server.** `Carton` (Domain, FEAT-003) sigue
  siendo inmutable — no se le agrega ningún campo de "reservado"/"vendido" (chocaría con la
  invariante ya documentada en `Carton.cs`, encontrado por impact scan). La reserva es un concepto
  de Redis con TTL, no una columna.
- **Estructura de claves Redis** (namespacing por dos-puntos, convención estándar de Redis):
  - `carrito:{sesionId}` — Hash `{cartonId (string) → precioUnitario (string, decimal invariant)}`.
    TTL de toda la clave = 300s, reiniciado en cada agregado exitoso (FR-06/FR-07). Su desaparición
    por TTL implementa FR-08 sin ningún proceso de limpieza — Redis expira la clave sola.
  - `reservado:carton:{cartonId}` — string, valor = `sesionId` que lo reservó. Mismo TTL de 300s,
    refrescado junto con el carrito.
  - `descartados:{sesionId}` — Set de `cartonId`. TTL propio de 30 minutos (constante
    `DescartadosTtlSegundos`), refrescado en cada llamada a "nueva tanda" — más largo que la reserva
    porque descartar ocurre típicamente antes de reservar nada. Valor elegido en PLAN, sin
    requisito del PRD maestro que lo fije; se documenta acá para no reabrirlo en CODE.
- **Reserva atómica vía script Lua (`EVAL`), no `MULTI`/`WATCH`**: un único round-trip que (1)
  verifica que `reservado:carton:{cartonId}` esté libre o ya pertenezca a esta sesión, (2) si está
  libre lo reserva y agrega al hash del carrito, (3) refresca el TTL de **todas** las reservas ya
  presentes en el carrito al mismo valor — necesario porque FR-07 exige que un agregado reinicie el
  plazo de *todo* el carrito, no solo del ítem nuevo. `MULTI`/`WATCH` con reintentos sería más
  código para el mismo resultado sin ganar nada, dado que el proyecto corre Redis standalone (no
  Cluster) — el script construye nombres de clave dinámicamente dentro de sí mismo para el paso 3,
  válido en modalidad standalone (documentado como límite explícito: si el proyecto migrara a Redis
  Cluster, este script necesitaría rediseñarse para declarar esas claves vía `KEYS[]`).
- **Sesión anónima: token opaco CSPRNG (`RandomNumberGenerator.GetBytes(32)`, base64url), no JWT.**
  No hay claims que transportar — la posesión del token ES la autorización para ese carrito, mismo
  modelo de confianza que cualquier session id. Cookie `bingocart_carrito`
  (`HttpOnly`/`Secure`/`SameSite=Strict`/`Path=/`, sin `Expires` — vive lo que dure la sesión del
  navegador, más corta que cualquier TTL de Redis), mismo patrón de transporte que `bingocart_auth`
  (FEAT-001b) pero sin reusar `AddJwtBearer`/`OnMessageReceived` — ese pipeline exige un JWT firmado
  con claims de organizador, no aplica acá. Leer/crear la cookie es responsabilidad del Controller
  (Block 3), mismo criterio ya documentado en `OrganizadoresController.Login`: "fijar la cookie es
  un detalle de transporte HTTP, no de negocio".
- **Precio snapshotteado al agregar, no recalculado en cada vista.** Si un organizador edita
  `CostoPorCarton` (FEAT-007) mientras el cartón está en un carrito ajeno, ese carrito sigue
  mostrando el precio con el que se agregó — evita que el monto total cambie por una acción de
  terceros mientras el participante decide si compra.
- **"ya fue vendido" (FR-10/AC-09 del PRD) no es verificable en este ticket.** `Compra` no existe
  todavía en el dominio (mismo motivo por el que FEAT-006 se pospuso) — `IBingoRepository.
  TieneComprasRegistradasAsync` ya documenta explícitamente "hoy siempre `false`". Este ticket
  implementa la mitad de FR-10 que sí es verificable hoy: rechazar un cartón ya reservado por otra
  sesión. No es una reducción de alcance del PRD (la condición sigue en el texto para cuando exista
  `Compra`) ni requiere loop a DEFINE — es una implementación parcial ya precedented en el propio
  código (`TieneComprasRegistradasAsync`).
- **Dos excepciones nuevas, no una genérica**, para separar semánticas HTTP distintas: cartón que no
  existe o cuyo bingo ya no está activo → 404 (`CartonInexistenteException`); cartón que existe pero
  está reservado por otra sesión → 409 (`CartonYaReservadoException`). Ambas en
  `Domain/Carritos/Exceptions/` — el carrito es lo que no puede completar la operación, aunque el
  motivo hable de un cartón.
- **Quitar un cartón es idempotente (204 sin importar si estaba o no en el carrito)** — no hay
  `CarritoNoEncontradoException` ni "cartón no estaba en el carrito": semántica estándar de `DELETE`,
  evita que el cliente tenga que manejar una carrera entre "ya lo había quitado" y "todavía está".
- **"Nueva tanda" reutiliza `IDescubrimientoRepository` (FEAT-008a) extendido con un parámetro de
  exclusión**, no un mecanismo paralelo de selección aleatoria: agrega `IReadOnlyCollection<Guid>
  excluirCartonIds` a `ObtenerAleatoriosGlobalAsync`/`ObtenerAleatoriosDeBingoAsync` (`WHERE c.Id NOT
  IN (...)` sumado al filtro de "activo" ya existente). Los dos call sites existentes en
  `DescubrimientoService` (que no necesitan exclusión) pasan `Array.Empty<Guid>()` — sin cambio de
  comportamiento para FEAT-008a, ya en `main`.
- **Nuevo método `ObtenerParaCarritoAsync` en `IBingoRepository`** (no un puerto nuevo): dado un
  `cartonId`, devuelve el cartón + `CostoPorCarton`/`NombreEvento`/`NombreOrganizacion` de su bingo
  si ese bingo sigue activo (`FechaSorteoUtc > ahoraUtc`, mismo criterio ya usado en FEAT-005/007/
  008a), o `null` si el cartón no existe o su bingo ya venció. Vive en `IBingoRepository` porque es
  una consulta sobre `Bingo`/`Carton` (el agregado que ese puerto ya administra), no sobre el
  carrito en sí.

## Block 1 — Domain + Infraestructura Redis (núcleo del carrito)

**Files**
- `backend/BingoCart.Domain/Carritos/ItemCarrito.cs` (new) — `sealed record ItemCarrito(Guid
  CartonId, decimal PrecioUnitario)`.
- `backend/BingoCart.Domain/Carritos/Carrito.cs` (new) — agregado puro, sin I/O:
  ```csharp
  public sealed class Carrito
  {
      public IReadOnlyList<ItemCarrito> Items { get; }
      public int CantidadTotal => Items.Count;
      public decimal MontoTotal => Items.Sum(i => i.PrecioUnitario);

      private Carrito(IReadOnlyList<ItemCarrito> items) => Items = items;

      public static Carrito DeItems(IReadOnlyList<ItemCarrito> items) => new(items);
  }
  ```
- `backend/BingoCart.Domain/Carritos/Exceptions/CartonYaReservadoException.cs` (new) — hereda
  `DomainException`.
- `backend/BingoCart.Domain/Carritos/Exceptions/CartonInexistenteException.cs` (new) — hereda
  `DomainException`.
- `backend/BingoCart.Application/Carritos/ICarritoRepository.cs` (new) — puerto:
  ```csharp
  Task<bool> IntentarAgregarAsync(string sesionId, Guid cartonId, decimal precioUnitario, TimeSpan ttl);
  Task QuitarAsync(string sesionId, Guid cartonId);
  Task<IReadOnlyList<ItemCarrito>> ObtenerItemsAsync(string sesionId);
  Task AgregarDescartadosAsync(string sesionId, IReadOnlyCollection<Guid> cartonIds, TimeSpan ttl);
  Task<IReadOnlySet<Guid>> ObtenerDescartadosAsync(string sesionId);
  ```
  `IntentarAgregarAsync` devuelve `false` si el cartón ya está reservado por otra sesión (Application,
  Block 2, traduce eso a `CartonYaReservadoException`) — el repositorio no lanza excepciones de
  dominio, mismo criterio ya usado en `DescubrimientoRepository`.
- `backend/BingoCart.Infrastructure/Carritos/CarritoRepository.cs` (new) — implementa el puerto con
  `StackExchange.Redis` (`IConnectionMultiplexer`):
  - Script Lua embebido como `const string` (ver decisión de PLAN), invocado con
    `IDatabase.ScriptEvaluateAsync`. `KEYS[1] = "reservado:carton:{cartonId}"`, `KEYS[2] =
    "carrito:{sesionId}"`; `ARGV = [sesionId, cartonId, precioUnitario, ttlSegundos]`.
  - `QuitarAsync`: `HDEL carrito:{sesionId} cartonId` + `DEL reservado:carton:{cartonId}` en una
    transacción `IDatabase.CreateTransaction()` (atomicidad entre las dos, no requiere Lua) — **sin
    `EXPIRE` en ningún lado** (NFR-03).
  - `ObtenerItemsAsync`: `HGETALL carrito:{sesionId}` → `IReadOnlyList<ItemCarrito>`.
  - `AgregarDescartadosAsync`: `SADD descartados:{sesionId} ...cartonIds` + `EXPIRE
    descartados:{sesionId} ttl` (acá sí se refresca el TTL en cada llamada — es la clave de
    descartados, no la del carrito).
  - `ObtenerDescartadosAsync`: `SMEMBERS descartados:{sesionId}`.
- `backend/BingoCart.Infrastructure/BingoCart.Infrastructure.csproj` (modified) — agrega paquete
  NuGet `StackExchange.Redis`.
- `docker-compose.yml` (modified) — nuevo servicio `redis` (imagen `redis:7-alpine`, puerto
  `16379:6379` mismo criterio de remapeo que `db`/`14330` para no chocar con un Redis local del
  desarrollador, sin volumen — el carrito es efímero por diseño, perderlo en un restart del
  contenedor es aceptable). `api` depende de `redis` además de `db`; variable de entorno
  `Redis__ConnectionString`.
- `backend/BingoCart.Api/appsettings.Development.json` (modified) — `"Redis": {
  "ConnectionString": "localhost:16379" }`.
- `backend/BingoCart.Api/Program.cs` (modified, parcial — el resto en Block 3) — registra
  `IConnectionMultiplexer` como singleton (`ConnectionMultiplexer.Connect(...)`, leyendo
  `Redis:ConnectionString`) y `AddScoped<ICarritoRepository, CarritoRepository>()`.

**Logic**
`Carrito` (Domain) es aritmética pura sobre una lista ya cargada — no decide qué está o no
reservado, eso lo resuelve Redis vía el script Lua antes de que exista un `Carrito` en memoria.
`CarritoRepository` es infraestructura pura: no valida que el `cartonId` corresponda a un cartón
real ni que su bingo siga activo (eso lo hace Block 2 contra SQL antes de llamar a este repositorio).

**API contract**
N/A — este bloque no expone ningún endpoint.

**Data model**
Sin cambios de esquema SQL. Estructura de claves Redis documentada arriba (decisiones de PLAN).

**Input validation**
`sesionId`/`cartonId`/`precioUnitario` llegan ya validados por el llamador (Application, Block 2).

**Error handling**
Este bloque no lanza excepciones de dominio — señaliza con el valor de retorno (`bool` de
`IntentarAgregarAsync`). Una falla de conexión a Redis se propaga sin capturar (cae en el `catch
(Exception ex)` genérico del middleware, 500), mismo criterio que una falla de SQL Server no
controlada.

**Required tests**
- [ ] `CarritoTests` (Domain): `Carrito.DeItems` con 3 `ItemCarrito` de precios 100/200/300 →
  `CantidadTotal == 3`, `MontoTotal == 600` — valida FR-09 (parte de dominio).
- [ ] `CarritoTests` (Domain): `Carrito.DeItems` con lista vacía → `CantidadTotal == 0`,
  `MontoTotal == 0`.
- [ ] `CarritoRepositoryTests` (Infrastructure, integración contra Redis real): `IntentarAgregarAsync`
  con un cartón libre → `true`, y `ObtenerItemsAsync` lo incluye con el precio correcto — valida
  FR-01/FR-04 (parte de infraestructura).
- [ ] `CarritoRepositoryTests`: `IntentarAgregarAsync` sobre un `cartonId` ya reservado por **otra**
  `sesionId` → `false`, el carrito de la sesión que intentó no lo incluye — valida FR-10 (parte de
  infraestructura).
- [ ] `CarritoRepositoryTests`: `IntentarAgregarAsync` de un segundo cartón en la misma sesión →
  `ObtenerItemsAsync` devuelve ambos, y el TTL de la clave `reservado:carton:{primerCartonId}` se
  refrescó (verificado con `IDatabase.KeyTimeToLiveAsync`, cercano al `ttl` pasado en el segundo
  agregado, no al original) — valida AC-06 (FR-06/FR-07).
- [ ] `CarritoRepositoryTests`: `IntentarAgregarAsync` con un `ttl` corto (1 segundo, parámetro
  explícito del método — no una constante hardcodeada) → tras esperar poco más de 1 segundo,
  `ObtenerItemsAsync` de esa sesión devuelve lista vacía y un `IntentarAgregarAsync` posterior de
  **otra** sesión sobre el mismo `cartonId` devuelve `true` (quedó libre) — valida AC-07 (FR-08),
  liberación automática por expiración de TTL sin ningún proceso de limpieza.
- [ ] `CarritoRepositoryTests`: `QuitarAsync` de un cartón en un carrito con otros 2 ítems cuyo TTL
  vence en un instante conocido → el TTL de `carrito:{sesionId}` y de los `reservado:carton:*`
  restantes NO cambia tras el `QuitarAsync` (mismo valor de `KeyTimeToLiveAsync` antes y después,
  con margen de milisegundos) — valida AC-08 (NFR-03).
- [ ] `CarritoRepositoryTests`: `AgregarDescartadosAsync` con 2 `cartonId`, luego
  `ObtenerDescartadosAsync` → los devuelve; una segunda llamada con un tercer `cartonId` distinto →
  `ObtenerDescartadosAsync` devuelve los 3 acumulados — valida FR-03 (parte de infraestructura).
- [ ] `CarritoRepositoryTests` (concurrencia, NFR-01): dos `sesionId` distintas llaman
  `IntentarAgregarAsync` simultáneamente (`Task.WhenAll`) para el **mismo** `cartonId` → exactamente
  una devuelve `true`, la otra `false`; `ObtenerItemsAsync` de la sesión ganadora lo incluye, el de
  la perdedora no.

**Completion criterion**
Los 9 tests pasan contra un Redis real (`docker-compose up -d redis` o instancia local en
`localhost:16379`); ninguna operación de este bloque toca SQL Server; `QuitarAsync` nunca modifica
el TTL de ninguna clave que no sea la que borra.

## Block 2 — Extensión de descubrimiento con exclusión + Application (`CarritoService`)

**Files**
- `backend/BingoCart.Application/Descubrimiento/IDescubrimientoRepository.cs` (modified) — agrega
  `IReadOnlyCollection<Guid> excluirCartonIds` como último parámetro de
  `ObtenerAleatoriosGlobalAsync` y `ObtenerAleatoriosDeBingoAsync`.
- `backend/BingoCart.Infrastructure/Descubrimiento/DescubrimientoRepository.cs` (modified) — suma
  `AND c.Id NOT IN ({string.Join(",", excluirCartonIds)})` a ambas queries `FromSqlInterpolated`
  cuando `excluirCartonIds` no está vacía (si está vacía, no agrega la cláusula — evita un `NOT IN
  ()` inválido en SQL Server).
- `backend/BingoCart.Application/Descubrimiento/DescubrimientoService.cs` (modified) — los dos call
  sites existentes pasan `Array.Empty<Guid>()`; sin cambio de comportamiento.
- `backend/BingoCart.Application/Bingos/IBingoRepository.cs` (modified) — agrega
  `Task<CartonParaCarrito?> ObtenerParaCarritoAsync(Guid cartonId, DateTime ahoraUtc);`.
- `backend/BingoCart.Infrastructure/Bingos/BingoRepository.cs` (modified) — implementa: JOIN
  `Cartones`+`Bingos`+`Users` filtrando `c.Id == cartonId && b.FechaSorteoUtc > ahoraUtc`, `null` si
  no hay resultado (cartón inexistente o bingo vencido — mismo criterio de no distinguir ambos casos
  ya usado en `BingoNoEncontradoException`).
- `backend/BingoCart.Application/Carritos/Dtos/CartonParaCarrito.cs` (new) — `sealed record
  CartonParaCarrito(Guid CartonId, Guid BingoId, decimal PrecioUnitario, string NombreOrganizacion,
  string NombreEvento)`.
- `backend/BingoCart.Application/Carritos/Dtos/ItemCarritoResponse.cs` (new) — `sealed record
  ItemCarritoResponse(Guid CartonId, string NombreOrganizacion, string NombreEvento, decimal
  PrecioUnitario)`.
- `backend/BingoCart.Application/Carritos/Dtos/CarritoResponse.cs` (new) — `sealed record
  CarritoResponse(IReadOnlyList<ItemCarritoResponse> Items, int CantidadTotal, decimal
  MontoTotal)`.
- `backend/BingoCart.Application/Carritos/ICarritoService.cs` (new) — puerto:
  ```csharp
  Task AgregarAsync(string sesionId, Guid cartonId);
  Task QuitarAsync(string sesionId, Guid cartonId);
  Task<CarritoResponse> ObtenerCarritoAsync(string sesionId);
  Task<IReadOnlyList<CartonDescubiertoResponse>> PedirNuevaTandaGlobalAsync(
      string sesionId, IReadOnlyCollection<Guid> descartadosDeEstaTanda);
  Task<IReadOnlyList<CartonDescubiertoResponse>> PedirNuevaTandaPorOrganizadorAsync(
      string sesionId, Guid organizadorId, IReadOnlyCollection<Guid> descartadosDeEstaTanda);
  ```
- `backend/BingoCart.Application/Carritos/CarritoService.cs` (new) — implementa, con `TimeProvider`
  inyectado (nunca `DateTime.UtcNow` directo):
  - Constantes privadas `ReservaTtl = TimeSpan.FromMinutes(5)`, `DescartadosTtl =
    TimeSpan.FromMinutes(30)`, `CantidadPorTanda = 5`.
  - `AgregarAsync`: `cartonInfo = await _bingoRepository.ObtenerParaCarritoAsync(cartonId,
    ahoraUtc)`; si `null` → `throw new CartonInexistenteException(...)`. `agregado = await
    _carritoRepository.IntentarAgregarAsync(sesionId, cartonId, cartonInfo.PrecioUnitario,
    ReservaTtl)`; si `false` → `throw new CartonYaReservadoException(...)`.
  - `QuitarAsync`: delega directo a `_carritoRepository.QuitarAsync` — idempotente, sin excepción.
  - `ObtenerCarritoAsync`: `items = await _carritoRepository.ObtenerItemsAsync(sesionId)`; arma
    `Carrito.DeItems` (Domain) para `CantidadTotal`/`MontoTotal`; para los nombres de
    organización/evento de cada ítem, resuelve contra `ObtenerResumenBingosAsync`
    (`IDescubrimientoRepository`, ya existe) por los `BingoId` distintos de los `cartonId` — **nota:**
    `ItemCarrito` (Domain, Block 1) no trae `BingoId`; el repositorio de Redis (Block 1) solo guarda
    `cartonId`→`precio`. Para resolver nombre de organización/evento sin volver a golpear SQL por
    cada ítem, `ObtenerCarritoAsync` reconsulta `ObtenerParaCarritoAsync` por cada `cartonId` del
    carrito (aceptable: un carrito nunca es grande, sin límite superior en el PRD pero uso esperado
    de pocas unidades — no se pagina).
  - `PedirNuevaTandaGlobalAsync`/`PedirNuevaTandaPorOrganizadorAsync`: `await
    _carritoRepository.AgregarDescartadosAsync(sesionId, descartadosDeEstaTanda, DescartadosTtl)`;
    `enCarrito = (await _carritoRepository.ObtenerItemsAsync(sesionId)).Select(i => i.CartonId)`;
    `descartadosHistoricos = await _carritoRepository.ObtenerDescartadosAsync(sesionId)`;
    `excluir = enCarrito.Union(descartadosHistoricos).ToList()`; delega al método de
    `IDescubrimientoRepository` correspondiente (global o por organizador, reutilizando la misma
    lógica de armado de respuesta que `DescubrimientoService`, sin duplicar el filtro de "activo").

**Logic**
`CarritoService` es el único punto que combina Redis (Block 1) y SQL (`IBingoRepository`,
`IDescubrimientoRepository`) — ninguno de los dos repositorios se conoce entre sí.

**API contract**
N/A — este bloque no expone ningún endpoint (eso es Block 3).

**Data model**
Sin cambios adicionales a los ya declarados en Block 1.

**Input validation**
`sesionId` se asume no vacío (el Controller, Block 3, lo garantiza antes de llamar). `cartonId`/
`organizadorId` son `Guid`, sin validación de formato adicional acá.

**Error handling**
`CartonInexistenteException` (cartón no existe o bingo vencido) y `CartonYaReservadoException`
(cartón reservado por otra sesión) — ambas nuevas en Block 1 (Domain), lanzadas acá.

**Required tests**
- [ ] `DescubrimientoRepositoryTests` (Infrastructure, existente de FEAT-008a, extendido):
  `ObtenerAleatoriosGlobalAsync` con `excluirCartonIds` conteniendo 2 de los 5 cartones elegibles →
  nunca los devuelve entre los resultados — valida la extensión de FR-03 en infraestructura.
- [ ] `DescubrimientoRepositoryTests`: `ObtenerAleatoriosGlobalAsync` con `excluirCartonIds` vacía →
  mismo comportamiento que antes de este ticket (sin regresión, FEAT-008a).
- [ ] `BingoRepositoryTests` (Infrastructure): `ObtenerParaCarritoAsync` con un cartón real de un
  bingo activo → devuelve `CostoPorCarton`/`NombreEvento`/`NombreOrganizacion` correctos; con un
  `cartonId` inexistente → `null`; con un cartón cuyo bingo tiene sorteo pasado → `null`.
- [ ] `CarritoServiceTests` (unit, mocks de `ICarritoRepository`/`IBingoRepository`/
  `IDescubrimientoRepository`): `AgregarAsync` con `ObtenerParaCarritoAsync` devolviendo un cartón
  válido e `IntentarAgregarAsync` devolviendo `true` → no lanza, invoca ambos con los parámetros
  correctos — valida AC-01 (orquestación).
- [ ] `CarritoServiceTests`: `AgregarAsync` con `ObtenerParaCarritoAsync` devolviendo `null` →
  `CartonInexistenteException`, sin invocar `IntentarAgregarAsync` — valida el caso "no existe /
  bingo vencido" de FR-10.
- [ ] `CarritoServiceTests`: `AgregarAsync` con `ObtenerParaCarritoAsync` válido pero
  `IntentarAgregarAsync` devolviendo `false` → `CartonYaReservadoException` — valida AC-09.
- [ ] `CarritoServiceTests`: `ObtenerCarritoAsync` con 3 ítems mockeados de precios 100/200/300 →
  `CarritoResponse.CantidadTotal == 3`, `MontoTotal == 600` — valida AC-04/FR-09.
- [ ] `CarritoServiceTests`: `ObtenerCarritoAsync` con carrito vacío → `CantidadTotal == 0`,
  `MontoTotal == 0`, `Items` vacío.
- [ ] `CarritoServiceTests`: `PedirNuevaTandaGlobalAsync` con 2 ítems ya en el carrito y 1
  descartado previamente → invoca `ObtenerAleatoriosGlobalAsync` con `excluirCartonIds` conteniendo
  los 3 (unión) — valida AC-03 (orquestación).
- [ ] `CarritoServiceTests`: `QuitarAsync` delega directo a `_carritoRepository.QuitarAsync` con los
  parámetros recibidos, sin lanzar — valida AC-05 (orquestación).

**Completion criterion**
Los 10 tests pasan; `DescubrimientoService` (FEAT-008a) sigue devolviendo exactamente el mismo
resultado que antes de este ticket para sus dos métodos existentes (regresión cero); ningún cartón
excluido por "ya en el carrito" o "ya descartado" aparece en una nueva tanda de la misma sesión.

## Block 3 — Api: `CarritoController`, cookie de sesión, rate limiting y mapeo de errores

**Files**
- `backend/BingoCart.Api/Controllers/CarritoController.cs` (new):
  ```csharp
  [AllowAnonymous]
  [ApiController]
  [Route("api/carrito")]
  public sealed class CarritoController : ControllerBase
  {
      private const string CookieName = "bingocart_carrito";
      private readonly ICarritoService _carritoService;

      [HttpPost("cartones/{cartonId:guid}")]
      [EnableRateLimiting("carrito")]
      public async Task<IActionResult> Agregar(Guid cartonId)
      {
          var sesionId = ObtenerOCrearSesionId();
          await _carritoService.AgregarAsync(sesionId, cartonId);
          return NoContent();
      }

      [HttpDelete("cartones/{cartonId:guid}")]
      [EnableRateLimiting("carrito")]
      public async Task<IActionResult> Quitar(Guid cartonId)
      {
          var sesionId = ObtenerOCrearSesionId();
          await _carritoService.QuitarAsync(sesionId, cartonId);
          return NoContent();
      }

      [HttpGet]
      [EnableRateLimiting("carrito")]
      public async Task<ActionResult<CarritoResponse>> Ver()
      {
          var sesionId = ObtenerOCrearSesionId();
          return Ok(await _carritoService.ObtenerCarritoAsync(sesionId));
      }

      [HttpPost("tandas/nueva")]
      [EnableRateLimiting("carrito")]
      public async Task<ActionResult<IReadOnlyList<CartonDescubiertoResponse>>> NuevaTanda(
          [FromBody] NuevaTandaRequest request)
      {
          var sesionId = ObtenerOCrearSesionId();
          var resultado = request.OrganizadorId is null
              ? await _carritoService.PedirNuevaTandaGlobalAsync(sesionId, request.CartonIdsDescartados)
              : await _carritoService.PedirNuevaTandaPorOrganizadorAsync(
                  sesionId, request.OrganizadorId.Value, request.CartonIdsDescartados);
          return Ok(resultado);
      }

      private string ObtenerOCrearSesionId()
      {
          if (Request.Cookies.TryGetValue(CookieName, out var existente) && !string.IsNullOrEmpty(existente))
          {
              return existente;
          }

          var nuevo = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
          Response.Cookies.Append(CookieName, nuevo, new CookieOptions
          {
              HttpOnly = true,
              Secure = true,
              SameSite = SameSiteMode.Strict,
              Path = "/"
          });
          return nuevo;
      }
  }
  ```
  Fijar/leer la cookie vive acá, no en Application — mismo criterio ya documentado en
  `OrganizadoresController.Login` (FEAT-001b): "detalle de transporte HTTP, no de negocio".
- `backend/BingoCart.Api/Contracts/NuevaTandaRequest.cs` (new) — `sealed record
  NuevaTandaRequest(Guid? OrganizadorId, IReadOnlyList<Guid> CartonIdsDescartados)`.
- `backend/BingoCart.Api/Middleware/ExceptionHandlingMiddleware.cs` (modified) — agrega dos `catch`:
  ```csharp
  catch (CartonInexistenteException ex)
  {
      await ManejarExcepcionDeDominioAsync(context, ex, HttpStatusCode.NotFound, "CartonInexistente");
  }
  catch (CartonYaReservadoException ex)
  {
      await ManejarExcepcionDeDominioAsync(context, ex, HttpStatusCode.Conflict, "CartonYaReservado");
  }
  ```
- `backend/BingoCart.Api/Program.cs` (modified, resto) — `AddScoped<IBingoRepository,
  BingoRepository>()` ya existe (sin cambio de registro, solo de implementación en Block 2);
  `AddScoped<ICarritoService, CarritoService>()`; nueva política de rate limiting:
  ```csharp
  options.AddPolicy("carrito", httpContext => RateLimitPartition.GetFixedWindowLimiter(
      partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
      factory: _ => new FixedWindowRateLimiterOptions
      {
          PermitLimit = 60,
          Window = TimeSpan.FromMinutes(5)
      }));
  ```

**API contract**
- `POST /api/carrito/cartones/{cartonId}` — sin body. Response 204: agregado. Response 404:
  `CartonInexistente`. Response 409: `CartonYaReservado`. Response 429: rate limit. Fija la cookie
  `bingocart_carrito` si no existía. Auth: ninguna (sesión anónima).
- `DELETE /api/carrito/cartones/{cartonId}` — sin body. Response 204 siempre (idempotente). Response
  429: rate limit.
- `GET /api/carrito` — sin params. Response 200: `{ "items": [{ "cartonId": "guid",
  "nombreOrganizacion": "string", "nombreEvento": "string", "precioUnitario": "number" }],
  "cantidadTotal": "number", "montoTotal": "number" }`. Carrito vacío (sesión nueva o sin cookie) →
  200 con `items: []`, `cantidadTotal: 0`, `montoTotal: 0`.
- `POST /api/carrito/tandas/nueva` — body: `{ "organizadorId": "guid | null",
  "cartonIdsDescartados": ["guid", ...] }`. `organizadorId: null` → Método 1 (global);
  `organizadorId` presente → Método 2. Response 200: misma forma que
  `GET /api/cartones/descubrimiento` (FEAT-008a). Response 429: rate limit.

**Input validation**
`cartonId:guid`/`organizadorId:guid` en ruta/body rechazan automáticamente valores no-Guid (routing/
model binding de ASP.NET Core, 400). `cartonIdsDescartados` no se valida por tamaño — una lista más
larga que la tanda real simplemente no encuentra coincidencias adicionales que excluir, sin efecto
dañino.

**Error handling**
`CartonInexistenteException` → 404 (nueva). `CartonYaReservadoException` → 409 (nueva). Sin otros
errores de dominio nuevos en este bloque.

**Required tests**
- [ ] `CarritoControllerTests` (integración, `WebApplicationFactory` + SQL Server + Redis reales): un
  bingo activo con cartones reales sembrados, `POST /api/carrito/cartones/{id}` de un cartón real
  sin cookie previa → 204, la respuesta fija la cookie `bingocart_carrito` — valida AC-01/AC-02.
- [ ] `CarritoControllerTests`: agregar un cartón inexistente (`Guid.NewGuid()`) → 404
  `CartonInexistente`.
- [ ] `CarritoControllerTests`: dos clientes de prueba (dos `HttpClient` con distinto `CookieContainer`,
  sin cookie compartida) agregan el mismo `cartonId` → el segundo recibe 409 `CartonYaReservado` —
  valida AC-09 end-to-end.
- [ ] `CarritoControllerTests`: agregar 2 cartones con la misma cookie, luego `GET /api/carrito` →
  200 con 2 ítems, `cantidadTotal: 2`, `montoTotal` igual a la suma de sus `CostoPorCarton` — valida
  AC-04.
- [ ] `CarritoControllerTests`: `GET /api/carrito` sin cookie previa (sesión nunca usada, mismo
  comportamiento observable que una sesión cuya cookie se perdió) → 200 con `items: []`,
  `cantidadTotal: 0` — valida AC-10.
- [ ] `CarritoControllerTests`: agregar 1 cartón, `DELETE /api/carrito/cartones/{id}` → 204, `GET
  /api/carrito` → `items: []` — valida AC-05.
- [ ] `CarritoControllerTests`: `DELETE` de un `cartonId` que nunca estuvo en el carrito → 204
  igualmente (idempotencia).
- [ ] `CarritoControllerTests`: `POST /api/carrito/tandas/nueva` con `organizadorId: null` y
  `cartonIdsDescartados` con los 5 de una tanda simulada previa → 200 con hasta 5 cartones nuevos,
  ninguno coincide con los descartados enviados — valida AC-03 end-to-end (Método 1).
- [ ] `CarritoControllerTests`: mismo caso con `organizadorId` de un organizador real con bingo
  activo → 200 con hasta 5 cartones, todos de ese bingo, ninguno coincide con los descartados —
  valida AC-03 end-to-end (Método 2).
- [ ] `CarritoControllerTests`: 61 requests consecutivas a `GET /api/carrito` desde el mismo cliente
  dentro de la ventana de 5 minutos → el request 61 devuelve 429 — valida NFR-02.

**Completion criterion**
Los 10 tests pasan; `docker-compose up --build` desde un clone limpio deja `redis` sano (`healthy`)
y la Api conecta sin intervención manual; un participante sin autenticar puede agregar, ver, quitar
y pedir una nueva tanda de cartones usando solo la cookie de sesión, sin que dos sesiones puedan
reservar el mismo cartón.

## Final verification

`dotnet build`/`dotnet test` sobre `backend/BingoCart.sln` en verde, incluyendo los 29 tests
automatizados nuevos de los Blocks 1-3 (9+10+10) más los 2 tests de infraestructura extendidos de
FEAT-008a (exclusión, sin regresión). Un participante sin registrarse arma un carrito de cartones de
uno o varios organizadores, lo ve con su total, quita ítems y pide tandas nuevas sin repetir
descartes, con una reserva de 5 minutos que protege contra doble venta y se libera sola si abandona.
Ningún frontend se toca en este ticket (backend-only, mismo criterio que FEAT-003/004/005/007/008a).
