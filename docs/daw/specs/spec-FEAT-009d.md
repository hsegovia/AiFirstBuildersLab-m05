# Spec FEAT-009d: "Mis cartones" del comprador y actualización de datos de cuenta

| Field | Value |
|-------|-------|
| Ticket | FEAT-009d |
| PRD | docs/daw/prd/prd-FEAT-009d.md |
| Tier | FEATURE |
| Date | 2026-08-23 |
| Spec loops | 3 |

## Summary

Se agrega a `Carton` un número correlativo por bingo, asignado en la generación y propagado a todas
las proyecciones que el comprador ve, con migración y backfill determinista de los cartones ya
existentes. Sobre eso se construyen las dos capacidades del ticket: un listado paginado de los
cartones que el comprador adquirió, con descarga on-demand del PDF de cualquiera de ellos —el primer
endpoint binario del proyecto—, y la actualización de sus datos de cuenta contra ASP.NET Core
Identity, protegida por la contraseña actual y bloqueada cuando alguno de sus sorteos ocurre dentro
de los próximos 60 minutos. Las dos mitades se implementan en archivos separados (D-03).

## Coverage: PRD → blocks

| Requirement | Covered by |
|---|---|
| FR-01 | Block 3 |
| FR-02 | Block 3 |
| FR-03 | Block 3 |
| FR-04 | Block 4 |
| FR-05 | Block 3, Block 4 |
| FR-06 | Block 5, Block 6 |
| FR-07 | Block 5 |
| FR-08 | Block 5 |
| FR-09 | Block 5 |
| FR-10 | Block 5 |
| FR-11 | Block 1, Block 2 |
| FR-12 | Block 2, Block 3, Block 4 |
| FR-13 | Block 5, Block 6 |
| NFR-01 | Strategy: clamp `Math.Min(pageSize, 50)` en el service de Block 3, replicando `CompraOrganizadorService.cs:78`. La respuesta devuelve el `PageSize` ya clampeado, no el pedido. |
| NFR-02 | Strategy: política de rate limiting nueva `"comprador-cuenta"` en `Program.cs`, 30 permits / ventana de 5 min, particionada por `ClaimTypes.NameIdentifier`, aplicada a los 3 endpoints del ticket (Block 3, 4 y 6). Justificación del quiebre de precedente en Block 6. |
| NFR-03 | Strategy: paginación obligatoria (máx. 50 por página) más el hecho verificado de que la consulta de Block 3 se apoya solo en índices existentes — `IX_Compras_CompradorId` (`AppDbContext.cs:163`), el índice de FK que EF Core crea por convención sobre `CompraCartones.CompraId` (`AppDbContext.cs:178-182`), y las PK de `Cartones`, `Bingos` y `AspNetUsers`. No se agrega ningún índice nuevo; ver Block 3, "Data model". |
| NFR-04 | Strategy: en los 3 endpoints el id del actor sale exclusivamente de `Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!)` con `[Authorize(Roles = "Comprador")]`, precedente `ComprasController.cs:40,51`. Ninguna firma pública acepta `compradorId` por ruta, query ni cuerpo. |
| NFR-05 | Strategy: `TimeProvider` inyectado en `CompradorService` → `_timeProvider.GetUtcNow().UtcDateTime` → pasado como `DateTime ahoraUtc` al repositorio, patrón universal del proyecto (`BingoService.cs:31`, `CompraService.cs:75`, y 4 más). La comparación se hace contra `Bingo.FechaSorteoUtc`, que ya es UTC. |

## Dependencies between blocks

Orden de ejecución: **1 → 2 → 3 → 4**, y **5 → 6** en paralelo a partir del 1.

- **Block 2 depende de Block 1**: no puede propagar un campo que todavía no existe en la entidad.
- **Block 3 depende de Block 2**: el DTO de cartón adquirido incluye el correlativo (FR-12).
- **Block 4 depende de Block 2 y Block 3**: usa la firma ya extendida de `ICartonPdfRenderer` y el
  mismo criterio de pertenencia que el listado.
- **Block 5 no depende de los bloques 1-4**: la mitad "datos de cuenta" no toca el correlativo.
- **Block 6 depende de Block 5**: expone por HTTP lo que el 5 implementa.

Sin dependencias circulares.

**Archivos compartidos entre las dos mitades — declarados a propósito.** D-03 pide que las mitades no
compartan archivos y se cumple para toda la lógica, pero tres archivos son de registro global y no
tienen sustituto:

| Archivo | Mitad "cartones" | Mitad "cuenta" |
|---|---|---|
| `backend/BingoCart.Api/Program.cs` | registra el repositorio y el service de Block 3 | registra el repositorio de Block 5 y la política de rate limiting |
| `backend/BingoCart.Api/Middleware/ExceptionHandlingMiddleware.cs` | 1 catch nuevo (Block 4) | 3 catches nuevos (Block 6) |
| `backend/BingoCart.Infrastructure/Data/AppDbContext.cs` | Block 1 (correlativo) | — |

Son adiciones puntuales en listas ya existentes, sin lógica compartida: una revisión por mitades
sigue siendo posible.

---

## Block 1 — Correlativo de cartón: modelo, generación y migración

**Files**

- `backend/BingoCart.Domain/Bingos/Carton.cs` (modificado) — suma la propiedad `NumeroCorrelativo` y
  el parámetro correspondiente en el factory.
- `backend/BingoCart.Domain/Bingos/Exceptions/NumeroCorrelativoInvalidoException.cs` (nuevo) —
  excepción de dominio para el correlativo fuera de rango.
- `backend/BingoCart.Application/Bingos/BingoService.cs` (modificado) — líneas 57-59, único sitio de
  creación productivo de cartones.
- `backend/BingoCart.Infrastructure/Data/AppDbContext.cs` (modificado) — bloque de `Carton`, líneas
  101-145: configuración de la columna y el índice único compuesto.
- `backend/BingoCart.Infrastructure/Data/Migrations/{timestamp}_AddNumeroCorrelativoACartones.cs`
  (nuevo) — columna, backfill e índice.
- `backend/BingoCart.Infrastructure/Data/Migrations/AppDbContextModelSnapshot.cs` (modificado) — lo
  regenera EF Core, pero es archivo versionado y entra en el commit.
- `backend/tests/BingoCart.Domain.Tests/Bingos/CartonTests.cs` (modificado) — 5 call sites (líneas
  15, 27, 35, 43, 51) más los tests nuevos.
- `backend/tests/BingoCart.Infrastructure.Tests/Compras/EnvioMailRepositoryTests.cs` (modificado) —
  2 call sites (líneas 115, 116).
- `backend/tests/BingoCart.Infrastructure.Tests/Descubrimiento/DescubrimientoRepositoryTests.cs`
  (modificado) — helpers `NuevoCarton`/`NuevosCartones` (líneas 62, 68-79) y sus 13 sitios de
  siembra (105-107, 128, 155, 211-212, 230, 282, 310, 331, 367, 389, 430, 452).
- `backend/tests/BingoCart.Infrastructure.Tests/Bingos/BingoRepositoryTests.cs` (modificado) — helper
  de línea 66, siembras de 367 y 450, y **la corrección obligatoria del test de líneas 171-188**.
- `backend/tests/BingoCart.Application.Tests/Descubrimiento/DescubrimientoServiceTests.cs`
  (modificado) — 1 call site (línea 22).
- `backend/tests/BingoCart.Api.Tests/Controllers/CartonesControllerTests.cs` (modificado) — helper
  de líneas 92-101 y siembra de 138.
- `backend/tests/BingoCart.Api.Tests/Controllers/CarritoControllerTests.cs` (modificado) — call site
  163 y siembra 203.
- `backend/tests/BingoCart.Api.Tests/Controllers/ComprasControllerTests.cs` (modificado) — call sites
  183 y 315, siembras 212 y 320.

**Logic**

`Carton` es `sealed class` con propiedades `{ get; private init; }`, constructor privado sin
parámetros (línea 22) y un único factory `Crear` (línea 35) que construye por object initializer.
Agregar `NumeroCorrelativo` como `int { get; private init; }` **no rompe el constructor** —EF Core lo
usa por reflexión— pero sí cambia la firma del factory a
`Crear(Guid bingoId, IReadOnlyList<int> numeros, int numeroCorrelativo)`, y eso rompe los 16 call
sites en compilación.

El factory valida `numeroCorrelativo >= 1` antes de construir, con el mismo estilo de guarda que ya
usa para los números. Es una invariante interna: ningún camino de entrada de usuario alcanza este
parámetro, porque el correlativo siempre lo genera el sistema.

La asignación 1..N ocurre en `BingoService.cs:57-59`, que hoy es
`conjuntos.Select(conjunto => Carton.Crear(bingo.Id, conjunto)).ToList()` sobre una lista ya
materializada. Pasa a la sobrecarga posicional de `Select`:
`(conjunto, i) => Carton.Crear(bingo.Id, conjunto, i + 1)`. **No se toca `ICartonNumberGenerator` ni
`CartonNumberGenerator`**: el CSPRNG produce únicamente los conjuntos de 10 números y el correlativo
es la posición en la lista devuelta, no un número generado — por eso convive con la prohibición de
`AGENTS.md` sin excepción alguna.

El conjunto de cartones de un bingo nunca cambia después de crearse (`EditarBingoRequest` no expone
`CantidadCartones`, y eliminar un bingo borra en cascada), así que el rango 1..N es estable y no hace
falta renumerar nunca.

**Data model**

Entidad `Carton`, tabla `Cartones`:

| Campo | Tipo | Constraints |
|---|---|---|
| `Id` | `uniqueidentifier` | PK, existente |
| `BingoId` | `uniqueidentifier` | FK a `Bingos`, cascade, existente |
| `NumerosSerializados` | `nvarchar(60)` | existente |
| `NumeroCorrelativo` | `int` | **NOT NULL, nuevo** |

Índices:

- `IX_Cartones_BingoId_NumerosSerializados` — UNIQUE, existente (`AppDbContext.cs:143-144`). Se
  conserva sin cambios.
- `IX_Cartones_BingoId_NumeroCorrelativo` — **UNIQUE, nuevo**. Réplica exacta en forma y motivación
  del anterior, que el propio `AppDbContext` documenta como "red de seguridad a nivel de esquema":
  garantiza AC-12 en la base y no solo en el código. El proyecto no tiene ningún
  `IEntityTypeConfiguration` —el mapeo es todo inline en `OnModelCreating`—, así que la configuración
  va ahí.

**Migración — patrón de tres pasos, no `defaultValue`.** El backfill necesita un valor distinto por
fila dentro de cada grupo, y `AddColumn(..., defaultValue: n)` pone el mismo valor en todas. Ese
descarte ya está razonado por escrito en el repo, en
`20260821222859_AddEnviosMailYConfirmacionId.cs:14-21`, cuya solución es el patrón a replicar:

1. `AddColumn<int>("NumeroCorrelativo", "Cartones", nullable: true)`
2. `migrationBuilder.Sql(...)` con
   `ROW_NUMBER() OVER (PARTITION BY BingoId ORDER BY Id)` sobre las filas existentes
3. `AlterColumn<int>("NumeroCorrelativo", "Cartones", nullable: false, oldNullable: true)`
4. `CreateIndex` único sobre `(BingoId, NumeroCorrelativo)` — después del backfill, nunca antes

El `ORDER BY Id` y su consecuencia se documentan **en el propio archivo de migración** (D-08, R-07):
`Cartones` tiene exactamente tres columnas y ninguna refleja el orden de inserción —no hay
`FechaCreacionUtc` ni IDENTITY—, y ordenar por `NumerosSerializados` dependería del collation, que
este proyecto no fija en ningún lado. `Id` es la única opción determinista y estable entre entornos.
Consecuencia asumida: los cartones preexistentes reciben su correlativo en orden de GUID, arbitrario
respecto de cuándo se generaron; los creados a partir de este ticket, en orden de generación.

`Down` revierte en orden inverso: elimina el índice y después la columna.

**Input validation**

El bloque no acepta entrada externa. La única validación es la invariante del factory:
`numeroCorrelativo` debe ser `>= 1`.

**Error handling**

- `NumeroCorrelativoInvalidoException` — se lanza si el factory recibe un correlativo menor a 1.
  **No se mapea en `ExceptionHandlingMiddleware`** a propósito: ningún camino de entrada de usuario
  alcanza ese parámetro, así que dispararla significaría un error de programación y un 500 es la
  respuesta correcta. Mapearla a un 4xx sugeriría que el cliente puede provocarla, y no puede.
- `DbUpdateException` por violación de `IX_Cartones_BingoId_NumeroCorrelativo` — no se captura: es la
  red de seguridad de esquema y debe fallar ruidosamente si la generación alguna vez asignara
  duplicados. Se verifica con test.
- Falla del backfill sobre datos preexistentes — la migración es transaccional por EF Core; si el
  `Sql` falla, el `AlterColumn` a NOT NULL no llega a ejecutarse y la migración no queda a medias.

**Required tests**

- [ ] `CartonTests.Crear_AsignaElNumeroCorrelativoRecibido` — el factory persiste el correlativo
- [ ] `CartonTests.Crear_ConCorrelativoCero_LanzaNumeroCorrelativoInvalidoException` — sad path,
      cubre `NumeroCorrelativoInvalidoException`
- [ ] `CartonTests.Crear_ConCorrelativoNegativo_LanzaNumeroCorrelativoInvalidoException` — sad path
- [ ] `BingoServiceTests.CrearBingo_AsignaCorrelativosDistintosDesde1HastaN` — valida AC-12
- [ ] `BingoRepositoryTests.Insertar_DosCartonesConMismoBingoYCorrelativo_LanzaDbUpdateException` —
      el índice único nuevo funciona contra SQL Server real
- [ ] `BingoRepositoryTests` (líneas 171-188, **corregido**) — el test de números duplicados debe
      seguir fallando por `IX_Cartones_BingoId_NumerosSerializados`, no por el índice nuevo: los dos
      cartones se siembran con correlativos **distintos**
- [ ] `MigracionCorrelativoTests.Down_RevierteColumnaEIndiceSinDejarEstadoParcial` — aplica la
      migración sobre una base con cartones preexistentes, ejecuta `Down` y verifica que la tabla
      queda con sus 3 columnas originales y sin el índice nuevo. Cubre el caso de error "falla del
      backfill" documentado arriba: si el `Sql` no completara, la columna no puede quedar NOT NULL
      sin backfill

**Completion criterion**

`dotnet test` de Domain + Application + Infrastructure + Api en verde con la migración aplicada;
`dotnet ef migrations script` de la migración nueva ejecuta sin error sobre una base con cartones
preexistentes y deja, para cada bingo, correlativos distintos de 1 a N; `dotnet ef database update`
seguido del `Down` deja el esquema como estaba.

---

## Block 2 — Propagación del correlativo a las proyecciones existentes y al PDF

Depende de Block 1.

**Files**

- `backend/BingoCart.Application/Descubrimiento/Dtos/CartonDescubiertoResponse.cs` (modificado) —
  líneas 9-15.
- `backend/BingoCart.Infrastructure/Descubrimiento/DescubrimientoRepository.cs` (modificado) — las
  proyecciones de las dos consultas. Los `FromSqlRaw` de líneas 60 y 96 usan `SELECT TOP ({0}) c.*`,
  así que la columna nueva se materializa sola y **el SQL no cambia**.
- `backend/BingoCart.Application/Compras/Dtos/CartonParaMail.cs` (modificado) — línea 8.
- `backend/BingoCart.Infrastructure/Compras/EnvioMailRepository.cs` (modificado) — donde se arma
  `CartonParaMail`.
- `backend/BingoCart.Application/Carritos/Dtos/ItemCarritoResponse.cs` (modificado) — líneas 9-13.
- `backend/BingoCart.Application/Carritos/Dtos/CartonParaCarrito.cs` (modificado) — líneas 11-16.
- `backend/BingoCart.Application/Carritos/CarritoService.cs` (modificado) — el mapeo entre ambos.
- `backend/BingoCart.Application/Compras/Dtos/CartonParaConfirmarCompra.cs` (modificado) — líneas
  9-15.
- `backend/BingoCart.Infrastructure/Bingos/BingoRepository.cs` (modificado) —
  `ObtenerParaConfirmarCompraAsync`, líneas 96-113.
- `backend/BingoCart.Application/Compras/ICartonPdfRenderer.cs` (modificado) — línea 14, la firma.
- `backend/BingoCart.Infrastructure/Notificaciones/QuestPdfCartonRenderer.cs` (modificado) — línea
  16, única implementación.
- `backend/BingoCart.Application/Compras/EnvioMailService.cs` (modificado) — línea 130, único
  consumidor productivo del renderer.
- `backend/tests/BingoCart.Infrastructure.Tests/Notificaciones/QuestPdfCartonRendererTests.cs`
  (modificado).
- Los archivos de test de las proyecciones tocadas: `DescubrimientoRepositoryTests.cs`,
  `DescubrimientoServiceTests.cs`, `CarritoControllerTests.cs`, `CartonesControllerTests.cs`,
  `ComprasControllerTests.cs`, `EnvioMailServiceTests.cs` (modificados).

**Logic**

FR-12 lleva el correlativo a las cinco proyecciones existentes de cartón y al PDF, para que el
comprador vea el mismo número en todo el recorrido: descubrimiento → carrito → confirmación → mail →
mis cartones → PDF.

`ICartonPdfRenderer.Renderizar` pasa de `(Guid cartonId, IReadOnlyList<int> numeros)` a incluir el
correlativo. Es síncrono y puro, con una sola implementación y un solo consumidor productivo, así que
el cambio de firma es acotado.

**Invariante que este bloque debe preservar, no romper:** el correlativo viaja siempre de salida y
**nunca de entrada**. Ninguna ruta, query string ni request DTO lo acepta. Hoy todas las rutas con
identificador de cartón usan constraint `:guid` (`CarritoController.cs:38,55`,
`BingosController.cs:81,103`, `ComprasController.cs:78,102`, `CartonesController.cs:49`) y
`NuevaTandaRequest.cs:34` es `IReadOnlyList<Guid>`; ese estado se conserva intacto.

**API contract**

Este bloque no crea endpoints, pero **modifica el cuerpo de respuesta de tres endpoints ya
existentes**, agregando en cada uno un único campo `NumeroCorrelativo` (int, siempre presente, nunca
nulo) dentro de la representación de cada cartón. Ni la ruta, ni el método, ni el request, ni los
códigos de error, ni la autorización cambian en ninguno de los tres:

| Método y ruta | Dónde aparece el campo | Auth (sin cambios) |
|---|---|---|
| `GET /api/cartones/...` (descubrimiento, `CartonesController.cs:49`) | cada ítem de `CartonDescubiertoResponse` | `[AllowAnonymous]` |
| `GET`/`POST`/`DELETE` `/api/carrito/...` (`CarritoController.cs:38,55`) | cada ítem de `ItemCarritoResponse` | `[AllowAnonymous]` |
| `POST /api/compras/...` (confirmación) | cada ítem de `CartonParaConfirmarCompra` | `[Authorize(Roles = "Comprador")]` |

El cambio es **aditivo y compatible hacia atrás**: un consumidor que ignore el campo nuevo sigue
funcionando. `CartonParaMail` y `CartonParaCarrito` no son contratos HTTP —son DTO internos del mail
y del carrito— y por eso no figuran en esta tabla.

**Input validation**

Ninguna: el bloque solo agrega un campo de salida. Ningún contrato de entrada cambia, y esa ausencia
es deliberada: es exactamente lo que AC-13 y AC-15 verifican.

**Error handling**

- Un cartón cuyo correlativo no se puede resolver no es un caso posible: la columna es NOT NULL
  desde Block 1 y el backfill cubre todas las filas preexistentes. No se agrega manejo defensivo
  para un estado que el esquema impide.
- Una solicitud que use el correlativo donde se espera un `Guid` es rechazada por el constraint de
  ruta `:guid` de ASP.NET Core, que devuelve 404 sin llegar al controller. Se verifica con test.
- El descubrimiento ignora cualquier intento de usar el correlativo como criterio de filtro: no
  existe tal parámetro en su contrato, de modo que se descarta en el binding. Se verifica con test.

**Required tests**

- [ ] `DescubrimientoRepositoryTests` — la proyección de cartón descubierto incluye el correlativo
- [ ] `CarritoControllerTests` — el ítem del carrito incluye el correlativo
- [ ] `ComprasControllerTests` — la confirmación de compra incluye el correlativo
- [ ] `EnvioMailServiceTests` — el cartón del mail incluye el correlativo
- [ ] `QuestPdfCartonRendererTests.Renderizar_IncluyeElNumeroCorrelativoEnElDocumento` — valida AC-04
- [ ] Test de consistencia: un mismo cartón muestra el **mismo** correlativo en descubrimiento,
      carrito y mail — las tres superficies que existen al cerrar este bloque. La cuarta que AC-14
      exige, el listado de cartones adquiridos, no existe hasta Block 3, así que **AC-14 se valida
      allí**, entero, sobre las cuatro
- [ ] Test de que una ruta que recibe el correlativo en lugar del GUID no devuelve ningún cartón —
      valida AC-13, sad path
- [ ] Test de que el descubrimiento no acepta el correlativo como criterio de búsqueda ni selección —
      valida AC-15, sad path

**Completion criterion**

Las cinco proyecciones y el PDF exponen el correlativo; el test de consistencia demuestra que es el
mismo número en las tres superficies disponibles en este bloque —descubrimiento, carrito y mail—;
ningún contrato de entrada lo acepta, verificado por los dos tests de AC-13 y AC-15; suite backend
completa en verde.

---

## Block 3 — Mis cartones: listado paginado

Depende de Block 2. Mitad "cartones".

**Files**

- `backend/BingoCart.Application/Compras/ICompraRepository.cs` (modificado) — suma el listado de
  cartones del comprador.
- `backend/BingoCart.Infrastructure/Compras/CompraRepository.cs` (modificado) — implementación.
- `backend/BingoCart.Application/Compras/CartonesAdquiridosPaginados.cs` (nuevo) — record
  `(IReadOnlyList<CartonAdquiridoResponse> Items, int Total)`.
- `backend/BingoCart.Application/Compras/Dtos/CartonAdquiridoResponse.cs` (nuevo).
- `backend/BingoCart.Application/Compras/Dtos/MisCartonesResponse.cs` (nuevo).
- `backend/BingoCart.Application/Compras/Dtos/ListarMisCartonesQuery.cs` (nuevo).
- `backend/BingoCart.Application/Compras/IMisCartonesService.cs` (nuevo) y
  `MisCartonesService.cs` (nuevo).
- `backend/BingoCart.Api/Controllers/MisCartonesController.cs` (nuevo) — controller propio, **no** se
  agrega a `CompradoresController`, para que la mitad "cartones" no comparta archivo con la mitad
  "cuenta" (D-03).
- `backend/BingoCart.Api/Program.cs` (modificado) — registro DI del service.
- `backend/tests/BingoCart.Application.Tests/Compras/MisCartonesServiceTests.cs` (nuevo).
- `backend/tests/BingoCart.Infrastructure.Tests/Compras/CompraRepositoryTests.cs` (modificado).
- `backend/tests/BingoCart.Api.Tests/Controllers/MisCartonesControllerTests.cs` (nuevo).

**Logic**

Consulta con join de cuatro tablas: `CompraCartones` → `Compras` (por `CompraId`) → `Cartones` (por
`CartonId`) → `Bingos` → `AspNetUsers`, filtrando por `Compras.CompradorId`.

El precedente a seguir **no** es ninguno de los dos listados paginados obvios:
`BingoRepository.ListarPorOrganizadorAsync` es de una sola tabla y
`CompraRepository.ListarPorOrganizadorAsync` compone dos consultas en memoria. El precedente real es
`BingoRepository.ObtenerParaConfirmarCompraAsync` (líneas 96-113: join LINQ tipado
`Cartones → Bingos → Users` con proyección) combinado con la paginación de
`DirectorioRepository.ListarActivosAsync` (líneas 28-47: `.Join(_context.Users, ...)` + `CountAsync` +
`Skip`/`Take` + `Select` proyectado, sin materializar nunca `ApplicationUser`).

Forma canónica de paginación, idéntica en los cuatro repositorios existentes: query base →
`CountAsync()` → `OrderBy…().Skip((page - 1) * pageSize).Take(pageSize)` → record `XPaginado(Items,
Total)`. El clamp vive en el service (`Math.Min(pageSize, 50)`, precedente
`CompraOrganizadorService.cs:78`), y `TotalPaginas` se calcula con `Math.Ceiling` y guarda de
`Total == 0` (precedente `CompraOrganizadorService.cs:90-92`).

**Orden:** `CompraCarton` no tiene ninguna columna de fecha, así que el criterio sale de
`Compra.FechaCreacionUtc` vía join, descendente. Como esa columna no es única, se desempata por
`Carton.NumeroCorrelativo` ascendente, para que la paginación sea estable entre páginas —sin
desempate, dos cartones de la misma compra podrían repetirse u omitirse al pasar de página.

**Se listan los cartones de compras en cualquier estado**, incluidas las canceladas, cada uno con el
estado de pago de su compra (FR-02, AC-02, A-01). Este bloque **no** replica el filtro de FEAT-009c
que excluye `Cancelado`: ese filtro es para disponibilidad de venta, y acá el criterio es el
opuesto —el comprador necesita ver qué pasó con su compra—.

El listado no depende en absoluto del estado del outbox de mail (AC-10): no consulta `EnviosMail`.

**API contract**

- Método y ruta: `GET /api/compradores/mis-cartones`
- Request (query string): `Page` (int, default 1, `[Range(1, int.MaxValue)]`), `PageSize` (int,
  default 20, `[Range(1, int.MaxValue)]`) — mismo `sealed record` posicional que
  `ListarComprasQuery.cs:11-13`
- Response 200 (`MisCartonesResponse`): `Items` (`IReadOnlyList<CartonAdquiridoResponse>`), `Total`
  (int), `TotalPaginas` (int), `Page` (int), `PageSize` (int, **clampeado**)
- `CartonAdquiridoResponse`: `CartonId` (Guid), `NumeroCorrelativo` (int), `Numeros`
  (`IReadOnlyList<int>`), `NombreBingo` (string), `NombreOrganizacion` (string), `EstadoCompra`
  (string), `CompraId` (Guid)
- Códigos de error: 400 (`Page`/`PageSize` fuera de rango), 401 (sin autenticar), 403 (autenticado
  sin rol `Comprador`), 429 (rate limit)
- Auth: `[Authorize(Roles = "Comprador")]`, cookie httpOnly `bingocart_auth`. `compradorId` derivado
  exclusivamente del claim `NameIdentifier` (NFR-04)

**Data model**

No crea ni modifica ningún esquema. **No agrega índices, y eso es una decisión, no un olvido:** la
consulta se apoya únicamente en índices que ya existen —`IX_Compras_CompradorId`
(`AppDbContext.cs:163`) para el filtro, el índice de FK que EF Core crea por convención sobre
`CompraCartones.CompraId` a partir de la configuración de `AppDbContext.cs:178-182`, y las claves
primarias de `Cartones`, `Bingos` y `AspNetUsers` para los tres joins restantes—. Con el techo de 50
filas por página, NFR-03 se cumple sin esquema nuevo. Si la medición desmintiera esto, el índice se
agrega en su propio ticket con el número que la medición justifique.

**Input validation**

- `Page`: entero, mínimo 1, **default 1** si se omite → rechazo 400 vía `[Range]` si es menor.
  El default de 20 es el de `PageSize`, no el de `Page`.
- `PageSize`: entero, mínimo 1; valores mayores a 50 **no se rechazan**, se sirven con 50 (NFR-01,
  AC-03) — el mismo criterio que `ListarComprasQuery`, que tampoco rechaza el exceso en el DTO.

**Error handling**

- `Page`/`PageSize` fuera de rango → 400 con el código `"DatosInvalidos"`, por el mecanismo de
  `[Range]` ya vigente en el proyecto.
- Sin autenticación → 401; autenticado sin rol `Comprador` → 403. Ambos por el pipeline de
  `[Authorize]`, sin código propio.
- Un comprador sin ninguna compra → **200 con lista vacía y `Total = 0`**, no 404. No tener cartones
  es un estado válido, no un error, y `TotalPaginas` está guardado contra la división por cero.
- Exceder el rate limit → 429.

**Required tests**

- [ ] `MisCartonesControllerTests.Listar_ConCartonesAdquiridos_DevuelveTodosLosCamposEsperados` —
      valida AC-01 (correlativo, identificador, 10 números, bingo, organización, estado de pago)
- [ ] `MisCartonesControllerTests.Listar_ConComprasEnDistintosEstados_LasIncluyeTodasConSuEstado` —
      valida AC-02
- [ ] `MisCartonesServiceTests.ListarAsync_ConPageSizeMayorA50_ClampeaA50YDevuelveElTotalSinPaginar` —
      valida AC-03 y NFR-01
- [ ] `MisCartonesControllerTests.Listar_NoDevuelveCartonesDeOtroComprador` — valida AC-05 y R-01
- [ ] `MisCartonesControllerTests.Listar_ConEnvioDeMailFallido_DevuelveElListadoCompleto` — valida
      AC-10
- [ ] `MisCartonesControllerTests.Listar_ConPageCero_Devuelve400` — sad path de entrada inválida
- [ ] `MisCartonesControllerTests.Listar_SinRolComprador_Devuelve403` — sad path de autorización
- [ ] `MisCartonesControllerTests.Listar_SinAutenticar_Devuelve401` — sad path de autenticación
- [ ] `MisCartonesControllerTests.Listar_SuperandoElLimiteDeSolicitudes_Devuelve429` — la política
      `"comprador-cuenta"` aplica también a este endpoint, y particiona por comprador
- [ ] `MisCartonesControllerTests.Listar_SinCompras_Devuelve200ConListaVaciaYTotalCero` — sad path
      del caso vacío
- [ ] `CompraRepositoryTests.ListarCartonesDelComprador_OrdenaPorFechaDeCompraDescendente` — el orden
      y su desempate son estables
- [ ] `MisCartonesControllerTests.Listar_ElCorrelativoCoincideConElDeLasOtrasSuperficies` — test de
      consistencia end-to-end: un mismo cartón muestra el **mismo** correlativo en descubrimiento,
      carrito, mail y mis cartones — valida AC-14. Vive en este bloque y no en Block 2 porque es el
      primer punto del spec donde las cuatro superficies existen a la vez

**Completion criterion**

El endpoint devuelve 200 con los 7 campos de `CartonAdquiridoResponse` para un comprador con
cartones; un segundo comprador no ve ninguno de esos cartones en su propio listado; `pageSize=100`
responde con como máximo 50 ítems y el `PageSize` clampeado en el cuerpo; los 12 tests en verde.

---

## Block 4 — Descarga de PDF on-demand

Depende de Block 2 y Block 3. Mitad "cartones".

**Files**

- `backend/BingoCart.Api/Controllers/MisCartonesController.cs` (modificado) — segundo endpoint, mismo
  controller que el listado.
- `backend/BingoCart.Application/Compras/IMisCartonesService.cs` y `MisCartonesService.cs`
  (modificados) — método de obtención del cartón propio para renderizar.
- `backend/BingoCart.Application/Compras/ICompraRepository.cs` (modificado) — consulta de un cartón
  del comprador por id.
- `backend/BingoCart.Infrastructure/Compras/CompraRepository.cs` (modificado) — implementación.
- `backend/BingoCart.Domain/Compras/Exceptions/CartonNoEncontradoException.cs` (nuevo).
- `backend/BingoCart.Api/Middleware/ExceptionHandlingMiddleware.cs` (modificado) — 1 catch nuevo.
- `backend/tests/BingoCart.Api.Tests/Controllers/MisCartonesControllerTests.cs` (modificado).
- `backend/tests/BingoCart.Application.Tests/Compras/MisCartonesServiceTests.cs` (modificado).

**Logic**

Reutiliza `ICartonPdfRenderer` tal cual, con la firma ya extendida en Block 2: es síncrono, puro, sin
ningún contexto de mail, con implementación única `QuestPdfCartonRenderer` registrada `AddScoped` en
`Program.cs:108` y licencia QuestPDF ya inicializada en `Program.cs:119`. **Este ticket no construye
generación de PDF**; solo la expone on-demand.

Pertenencia (FR-05, AC-05, R-01): el service resuelve el cartón cruzando `CompraCartones` con las
compras del comprador autenticado. Si el cartón no existe **o** pertenece a otro comprador, lanza la
misma `CartonNoEncontradoException` con el mismo mensaje, sin distinguir los casos — el patrón
anti-enumeración ya establecido en `ObtenerCompraPropiaAsync` de FEAT-009c.

Se permite descargar el PDF de un cartón cuya compra fue cancelada (A-02): el PDF es comprobante de
lo que ocurrió, no título de propiedad vigente.

**El PDF se genera en memoria y no se persiste en ningún lado** — ni disco, ni caché, ni blob
storage: se compone, se devuelve y se descarta. Es una propiedad de seguridad que este bloque debe
conservar, no una casualidad de implementación (mitigación de R-03): sin almacenamiento no hay
archivo con datos de una persona identificada que alguien tenga que proteger, rotar o borrar después.
Además, el documento tiene **tamaño fijo** —siempre un cartón de 10 números—, de modo que no existe
amplificación por entrada: nadie puede pedir un PDF más caro de generar que otro.

**Es el primer endpoint binario del proyecto.** Un grep de `File(`, `FileContentResult`,
`FileStreamResult` y `application/pdf` sobre todos los `.cs` da cero resultados. Consecuencia a
documentar y respetar: la respuesta 200 sale con `Content-Type: application/pdf` vía
`File(bytes, "application/pdf", nombreArchivo)`, mientras que **el 404 sigue saliendo como JSON**,
porque `ExceptionHandlingMiddleware.cs:199` fija `ContentType = "application/json"` para todo lo que
pasa por él. Los dos content-types conviven en el mismo endpoint según el resultado, y eso es
deliberado.

**API contract**

- Método y ruta: `GET /api/compradores/mis-cartones/{cartonId:guid}/pdf`
- Request: `cartonId` en la ruta, `Guid` por constraint. Sin cuerpo, sin query string
- Response 200: cuerpo binario, `Content-Type: application/pdf`, `Content-Disposition: attachment`
  con nombre de archivo derivado del cartón
- Códigos de error: 400 (`cartonId` no parsea como Guid — lo resuelve el constraint de ruta como 404
  de routing antes del controller), 404 (`"CartonNoEncontrado"`, tanto si no existe como si es
  ajeno), 401, 403, 429
- Auth: `[Authorize(Roles = "Comprador")]`. `compradorId` derivado exclusivamente del claim
  `NameIdentifier` (NFR-04); **ningún parámetro de la firma lo recibe**

**Input validation**

- `cartonId`: `Guid`, validado por el constraint de ruta `:guid`. Un valor no-GUID —incluido un
  número correlativo— no llega al controller: el routing responde 404. Eso es exactamente lo que
  AC-13 exige y se verifica con test.

**Error handling**

- `CartonNoEncontradoException` → 404 con código `"CartonNoEncontrado"`, cuerpo JSON. **Mismo
  mensaje y mismo cuerpo** para el cartón inexistente y para el ajeno: distinguirlos permitiría
  enumerar cartones de otros compradores.
- La excepción se declara en `Domain.Compras.Exceptions`, no se reutiliza la
  `CartonInexistenteException` existente, que pertenece al bounded context `Carritos`
  (`ExceptionHandlingMiddleware.cs:138-141`). Al haber dos tipos con nombres parecidos en el
  middleware, se aplica el patrón de alias de `using` ya documentado en ese mismo archivo, líneas
  9-18.
- Falla del renderer → excepción no capturada → 500 por el catch genérico ya existente, sin filtrar
  detalles internos al cliente (`ExceptionHandlingMiddleware.cs:160-176`).
- Exceder el rate limit → 429.

**Required tests**

- [ ] `MisCartonesControllerTests.DescargarPdf_ConCartonPropio_Devuelve200ConContentTypePdf` — valida
      AC-04
- [ ] `MisCartonesControllerTests.DescargarPdf_ConCartonAjeno_Devuelve404` — valida AC-05, sad path
- [ ] `MisCartonesControllerTests.DescargarPdf_ConCartonInexistente_Devuelve404ConElMismoCuerpo` —
      valida AC-05, anti-enumeración: se compara el cuerpo con el del test anterior
- [ ] `MisCartonesControllerTests.DescargarPdf_ConCompraCancelada_PermiteLaDescarga` — valida A-02
- [ ] `MisCartonesControllerTests.DescargarPdf_ConNumeroCorrelativoEnLugarDelGuid_NoDevuelveCarton` —
      valida AC-13, sad path
- [ ] `MisCartonesControllerTests.DescargarPdf_SinRolComprador_Devuelve403` — sad path de
      autorización
- [ ] `MisCartonesControllerTests.DescargarPdf_SinAutenticar_Devuelve401` — sad path de autenticación
- [ ] `MisCartonesControllerTests.DescargarPdf_SuperandoElLimiteDeSolicitudes_Devuelve429` — el
      endpoint que más trabajo de CPU genera por request es el que más necesita el límite
- [ ] `MisCartonesServiceTests.ObtenerPdfAsync_ConRendererQueFalla_PropagaLaExcepcionSinFiltrarDetalles` —
      sad path del 500: la excepción del renderer no se traga ni se convierte en un 200 vacío

**Completion criterion**

El endpoint devuelve un PDF válido y no vacío para un cartón propio, con `Content-Type:
application/pdf`; el cartón ajeno y el inexistente devuelven respuestas 404 **byte a byte idénticas**;
los 9 tests en verde.

---

## Block 5 — Datos de cuenta: dominio, Identity y regla temporal

No depende de los bloques 1-4. Mitad "cuenta".

**Files**

- `backend/BingoCart.Application/Compradores/ICompradorIdentityGateway.cs` (modificado) — líneas
  15-28: suma actualización y lectura de los datos de la cuenta.
- `backend/BingoCart.Infrastructure/Identity/IdentityGateway.cs` (modificado) — implementación.
- `backend/BingoCart.Application/Compradores/ICompradorCuentaRepository.cs` (nuevo) — consulta de
  sorteo inminente. **Puerto propio, no se agrega a `ICompraRepository`**, para no compartir archivo
  con la mitad "cartones" (D-03).
- `backend/BingoCart.Infrastructure/Compradores/CompradorCuentaRepository.cs` (nuevo).
- `backend/BingoCart.Application/Compradores/CompradorService.cs` (modificado) — línea 25: el
  constructor pasa de 2 a 3 argumentos con la inyección de `TimeProvider`.
- `backend/BingoCart.Application/Compradores/Dtos/ActualizarCuentaRequest.cs` (nuevo).
- `backend/BingoCart.Application/Compradores/Dtos/CuentaCompradorResponse.cs` (nuevo).
- `backend/BingoCart.Domain/Compradores/Exceptions/PlazoModificacionVencidoException.cs` (nuevo).
- `backend/BingoCart.Domain/Compradores/Exceptions/MailEnUsoException.cs` (nuevo).
- `backend/BingoCart.Domain/Compradores/Exceptions/CuitEnUsoException.cs` (nuevo).
- `backend/BingoCart.Domain/Compradores/Exceptions/ContrasenaIncorrectaException.cs` (nuevo) —
  FR-13.
- `backend/BingoCart.Api/Program.cs` (modificado) — registro DI del repositorio nuevo.
- `backend/tests/BingoCart.Application.Tests/Compradores/CompradorServiceTests.cs` (modificado) —
  línea 35 y los 6 tests que construyen el service.
- `backend/tests/BingoCart.Infrastructure.Tests/Compradores/CompradorCuentaRepositoryTests.cs`
  (nuevo).

**Logic**

`Comprador` (Domain) es inmutable —todas sus propiedades son `private init`— y **no se persiste**: no
hay `DbSet<Comprador>` en `AppDbContext`. Los datos viven en `ApplicationUser`/`AspNetUsers`. La
actualización va sí o sí por el gateway de Identity; no se intenta mutar la entidad de Domain, que
seguiría siendo un objeto de construcción y validación.

`ICompradorIdentityGateway` es el puerto del **comprador** y hoy tiene solo `ExisteMailAsync`,
`CrearUsuarioAsync` y `AutenticarAsync`. Su hermano `IIdentityGateway` es el puerto del
**organizador**, y la única implementación `IdentityGateway` implementa ambas interfaces sobre el
mismo `UserManager`. Los métodos nuevos van exclusivamente en el puerto del comprador: el del
organizador no se toca. Application nunca ve `UserManager`, según `AGENTS.md`.

**Cambio de mail — la parte que rompe si se hace ingenuamente.** El alta setea los dos campos
(`IdentityGateway.cs:92-93`: `UserName = comprador.Mail, Email = comprador.Mail`), pero
`UserManager.SetEmailAsync` toca `Email` y `NormalizedEmail` y **nunca** `UserName` /
`NormalizedUserName`. El login usa `FindByEmailAsync`, así que no se rompe de inmediato; el problema
aparece después: `AspNetUsers` mantiene un índice único sobre `NormalizedUserName`, de modo que si
otra cuenta se registra más tarde con el mail viejo, `CreateAsync` falla y `ExisteMailAsync` —que
solo mira `Email`— no lo ve venir. La actualización debe por lo tanto tocar `UserName` y
`SecurityStamp` además de `Email`, tratando el mail como credencial y no como campo suelto (R-03).

**Regla de los 60 minutos** (FR-07, NFR-05, D-01, D-02). La ventana es `[ahora, ahora + 60 min]`:
solo bloquean los sorteos **futuros** dentro de la próxima hora; los ya pasados se ignoran por
completo, porque la lectura literal congelaría la cuenta para siempre tras la primera compra. Las
compras `Cancelado` quedan **excluidas** del cálculo: ya no tienen cartones vigentes y no pueden
generar la disputa que la regla previene.

El ancla es `Bingo.FechaSorteoUtc`, ya indexada (`AppDbContext.cs:98`), con tres precedentes de
comparación temporal en el proyecto. El "ahora" se inyecta con `TimeProvider` —patrón universal, sin
un solo `DateTime.UtcNow` en código productivo—, lo que obliga a sumar el tercer argumento al
constructor de `CompradorService` y a actualizar los 6 tests que lo construyen.

**Validación de CUIT** (FR-09, AC-09): se reutiliza `CuitValidator` tal cual, con sus dos métodos
**separados** `TieneLongitudValida` y `TieneDigitoVerificadorValido` — justamente lo que AC-09 pide
para poder informar cuál de las dos reglas falló. `Comprador.Crear` ya lo usa así. La excepción que
corresponde es `Domain.Compradores.Exceptions.CuitInvalidoException` (no la de `Organizadores`:
decisión de PLAN ya cerrada y documentada en el propio archivo), que ya está mapeada a 400
`"CuitInvalido"` en el middleware vía alias.

**Colisiones** (FR-08, FR-10): tanto el mail como el CUIT tienen índice único en `AspNetUsers`
(`AppDbContext.cs:60-61` para el CUIT). Hoy un duplicado explotaría como `DbUpdateException` → 500
genérico. Se detectan **antes** de persistir, con consulta explícita, y se traducen a excepciones de
dominio propias. La detección excluye al propio usuario: reenviar el mismo CUIT que ya se tiene no es
una colisión.

La actualización es **total, no parcial** (A-03): los cuatro campos viajan y se persisten juntos. Si
cualquier validación falla, **no se modifica ninguno** (AC-08, AC-11): todas las verificaciones
ocurren antes de la primera escritura.

**Verificación de contraseña (FR-13, AC-16) — y el orden importa.** La solicitud lleva un quinto
campo, la contraseña actual, que **no es un dato de la cuenta**: es la prueba de identidad que
autoriza el cambio, se verifica y se descarta. Se comprueba contra Identity a través del puerto del
comprador, reutilizando el mismo mecanismo de verificación que ya usa `AutenticarAsync` —no se lee ni
se compara el `PasswordHash` a mano en ningún punto—.

Esta verificación es **la primera de todas**, antes de validar el CUIT y antes de consultar las
colisiones de mail y CUIT. El orden no es estético: si se verificara al final, alguien con una sesión
robada podría usar las respuestas "ese mail ya está en uso" / "ese CUIT ya está en uso" como oráculo
de enumeración sin conocer la contraseña. Verificando primero, ninguna de esas respuestas es
alcanzable sin haberla demostrado.

La contraseña **nunca se modifica** — este ticket no implementa cambio de contraseña, solo su
verificación— y **nunca se escribe en un log**, ni siquiera enmascarada.

**Input validation**

- `Apellido`: string, requerido, no vacío, máximo el largo ya vigente en el registro del comprador.
- `Nombre`: ídem.
- `Cuit`: string de exactamente 11 dígitos numéricos **y** dígito verificador válido, evaluados por
  separado para poder distinguir el motivo del rechazo.
- `Mail`: string, requerido, formato de dirección válida, mismo criterio que el registro.
- `ContrasenaActual`: string, requerido, no vacío. **No se valida su formato ni su longitud** —solo
  si coincide—: aplicar reglas de complejidad acá filtraría información sobre la contraseña
  almacenada.
- Los cuatro campos de datos son obligatorios: una solicitud parcial se rechaza, no se interpreta
  como actualización de lo enviado.

**Error handling**

- `ContrasenaIncorrectaException` (nueva) → la operación se rechaza sin tocar ningún dato y **sin
  evaluar ninguna otra validación**, de modo que la respuesta no revela nada sobre el mail ni el CUIT
  enviados (AC-16). El mensaje es genérico: no distingue "contraseña vacía" de "contraseña que no
  coincide".
- `CuitInvalidoException` → 400 `"CuitInvalido"`, ya mapeada. El mensaje indica **cuál** de las dos
  reglas falló (longitud o dígito verificador).
- `MailEnUsoException` (nueva) → conflicto: la operación se rechaza sin modificar ningún otro campo.
  **El mensaje no repite la dirección enviada**: decir "ese mail ya está en uso" cumple AC-08, y
  repetirla dejaría PII en la respuesta y en cualquier log que la capture (mitigación de R-04 del
  threat model).
- `CuitEnUsoException` (nueva) → conflicto: ídem, y **el mensaje no repite el CUIT enviado**. Un CUIT
  identifica unívocamente a una persona real, así que este caso es el más sensible de los dos.
- `PlazoModificacionVencidoException` (nueva) → la operación se rechaza informando que el plazo para
  modificar los datos ya venció.
- Las tres nuevas se lanzan **antes** de cualquier escritura, de modo que un rechazo nunca deja la
  cuenta parcialmente actualizada.
- Un fallo de `UserManager` al persistir (`IdentityResult` no exitoso) se traduce a excepción, no se
  ignora: un `IdentityResult` descartado sería un cambio que el usuario cree hecho y no está.
- Ningún log de este bloque incluye PII del comprador (mail, nombre, apellido, CUIT), la contraseña,
  ni `ex.Message`, según la disciplina ya establecida en FEAT-009b/c.
- **Log de auditoría del cambio (mitigación de R-05, repudio).** Una actualización exitosa registra
  `compradorId`, timestamp UTC y **la lista de nombres de los campos que cambiaron** — nunca sus
  valores, ni el anterior ni el nuevo. Sin esto, un cambio de apellido justo antes de un sorteo no
  deja ningún rastro y la disputa que FR-07 previene desde el tiempo queda abierta desde la
  trazabilidad. Un historial de valores previos sería un almacén nuevo de PII y queda fuera de
  alcance.

**Required tests**

- [ ] `CompradorServiceTests.ActualizarCuentaAsync_ConTodosLosSorteosLejanos_PersisteLosCambios` —
      valida AC-06
- [ ] `CompradorServiceTests.ActualizarCuentaAsync_ConSorteoDentroDe60Minutos_LanzaPlazoModificacionVencidoException` —
      valida AC-07, sad path
- [ ] `CompradorServiceTests.ActualizarCuentaAsync_ConSorteoYaPasado_PermiteLaActualizacion` — valida
      D-01, el caso que la lectura literal habría bloqueado para siempre
- [ ] `CompradorServiceTests.ActualizarCuentaAsync_ConCompraCanceladaYSorteoInminente_PermiteLaActualizacion` —
      valida D-02
- [ ] `CompradorServiceTests.ActualizarCuentaAsync_ConMailDeOtraCuenta_LanzaMailEnUsoExceptionSinModificarNada` —
      valida AC-08, sad path
- [ ] `CompradorServiceTests.ActualizarCuentaAsync_ConCuitDeOtraCuenta_LanzaCuitEnUsoExceptionSinModificarNada` —
      valida AC-11, sad path
- [ ] `CompradorServiceTests.ActualizarCuentaAsync_ConCuitDeLongitudInvalida_LanzaCuitInvalidoExceptionIndicandoLaLongitud` —
      valida AC-09, sad path
- [ ] `CompradorServiceTests.ActualizarCuentaAsync_ConDigitoVerificadorInvalido_LanzaCuitInvalidoExceptionIndicandoElDigito` —
      valida AC-09, sad path — el mensaje debe ser **distinguible** del anterior
- [ ] `CompradorServiceTests.ActualizarCuentaAsync_ConElMismoCuitDeLaPropiaCuenta_NoLoTrataComoColision` —
      sad path del falso positivo
- [ ] `CompradorServiceTests.ActualizarCuentaAsync_ConIdentityResultFallido_LanzaExcepcion` — sad path
      del `IdentityResult` descartado
- [ ] `IdentityGatewayTests.ActualizarDatos_ActualizaUserNameYSecurityStampAdemasDeEmail` — valida el
      riesgo R-03
- [ ] `CompradorCuentaRepositoryTests.TieneSorteoInminenteAsync_IgnoraComprasCanceladasYSorteosPasados` —
      la consulta implementa la ventana correcta
- [ ] `CompradorServiceTests.ActualizarCuentaAsync_ConContrasenaIncorrecta_LanzaContrasenaIncorrectaExceptionSinModificarNada` —
      valida AC-16 y FR-13, sad path
- [ ] `CompradorServiceTests.ActualizarCuentaAsync_ConContrasenaIncorrectaYMailYaEnUso_LanzaContrasenaIncorrectaException` —
      valida el **orden** de las verificaciones: con la contraseña mal, la respuesta no revela nada
      del mail enviado (AC-16, mitigación de R-04)
- [ ] `CompradorServiceTests.ActualizarCuentaAsync_ConMailEnUso_NoRepiteLaDireccionEnElMensaje` —
      mitigación de R-04
- [ ] `CompradorServiceTests.ActualizarCuentaAsync_ConCuitEnUso_NoRepiteElCuitEnElMensaje` —
      mitigación de R-04
- [ ] `CompradorServiceTests.ActualizarCuentaAsync_Exitosa_RegistraAuditoriaConNombresDeCamposYSinValores` —
      mitigación de R-05: el log lleva `compradorId`, timestamp y nombres de campo, y **no** contiene
      ningún valor de PII ni la contraseña

**Completion criterion**

Los 17 tests en verde; `CompradorService` no contiene ningún `DateTime.UtcNow` y recibe
`TimeProvider` por constructor; tras una actualización exitosa contra la base real, la fila de
`AspNetUsers` tiene `Email`, `NormalizedEmail`, `UserName`, `NormalizedUserName` y `SecurityStamp`
todos coherentes con el mail nuevo; con la contraseña incorrecta, ninguna de las otras validaciones
llega a ejecutarse y la fila queda intacta.

---

## Block 6 — Datos de cuenta: endpoint, cookie y middleware

Depende de Block 5. Mitad "cuenta".

**Files**

- `backend/BingoCart.Api/Controllers/CompradoresController.cs` (modificado) — hoy solo `registro`
  (línea 34) y `login` (línea 55); suma el `PUT`.
- `backend/BingoCart.Api/Middleware/ExceptionHandlingMiddleware.cs` (modificado) — 3 catches nuevos.
- `backend/BingoCart.Api/Program.cs` (modificado) — política de rate limiting nueva.
- `backend/tests/BingoCart.Api.Tests/Controllers/CompradoresControllerTests.cs` (modificado) — los
  tests nuevos **y la corrección obligatoria del `DisposeAsync` de las líneas 53-66**.

**Logic**

Endpoint `PUT` que recibe los cuatro campos, delega en `CompradorService` y devuelve el estado ya
actualizado de la cuenta. **No se crea un endpoint `GET` de perfil** (A-05): el proyecto no tiene uno
para el comprador —su propio doc-comment lo dice en las líneas 13-14— y el precedente del organizador
devuelve solo el mail desde el claim, así que no sirve de modelo. Devolver el estado en la respuesta
del `PUT` satisface AC-06 sin agregar superficie.

**Reemisión de la cookie** (D-05): `JwtTokenService.cs:33` emite `ClaimTypes.Email` con el mail del
login. Sin reemitir, la cookie vigente seguiría llevando el mail viejo hasta expirar. La autorización
usa `NameIdentifier`, así que nada se rompe funcionalmente, pero el token quedaría con un dato falso.
Tras una actualización exitosa se emite una cookie nueva con los claims frescos, reutilizando el
mismo mecanismo de emisión del login (`CompradoresController.cs:65-72`).

**Lo que la reemisión NO hace, y hay que dejarlo escrito** (mitigación de R-02). Este proyecto
autentica con **JWT Bearer stateless**: `Program.cs:157-163` fija ese esquema como default y un
evento `OnMessageReceived` extrae el token de la cookie. `AddIdentity` registra el esquema de cookie
de Identity, pero **no es el que autentica las requests**, y por lo tanto **no hay
`SecurityStampValidator` en el pipeline**. Consecuencia: rotar el `SecurityStamp` **no invalida
ningún token ya emitido** — las sesiones abiertas en otros dispositivos siguen siendo válidas hasta
que expiren solas, a los 60 minutos (`appsettings.json:18`).

La reemisión es por lo tanto **cosmética**: refresca el claim `Email` para que el token no cargue un
dato falso, y nada más. No se le atribuye un efecto de revocación que no tiene, y ningún comentario
de código debe sugerir lo contrario. El `SecurityStamp` se rota igual, porque es lo correcto para el
día en que se agregue validación de stamp, pero hoy no compra seguridad.

**Rate limiting (NFR-02) — quiebre de precedente, justificado.** Hay 8 políticas en `Program.cs`
(líneas 238-331) y ninguna sirve: `"compradores"` particiona por IP y es 5/min, pensada para
endpoints anónimos; `"compras"` particiona por claim pero es 10 req/5min, calibrada para el checkout.
Se crea `"comprador-cuenta"`: 30 permits, ventana de 5 minutos, particionada por
`ClaimTypes.NameIdentifier`, replicando la forma de `"compras-organizador"` (líneas 325-331).

Ahora bien: los GET de listado de este proyecto **hoy no llevan rate limiting**, y es una decisión
deliberada documentada en el código (`ComprasController.cs:122-124`: *"Sin rate limiting (mirror de
BingosController.Listar)"*). NFR-02 rompe ese precedente y la razón es que estos tres endpoints no son
equivalentes a aquellos: el de PDF **genera un documento por request** —trabajo de CPU no trivial, a
diferencia de una consulta paginada—, y el `PUT` es una mutación sobre credenciales de Identity, que
es exactamente el tipo de endpoint donde un límite por actor importa. Aplicar la política a los tres
mantiene el criterio coherente dentro del ticket. El precedente anterior no se toca: los listados ya
existentes siguen sin límite.

**Corrección obligatoria del cleanup de tests** (G-13): `DisposeAsync` (líneas 53-66) borra los
usuarios creados filtrando por `u.Email IN _mailsCreados`. Un test que **cambie** el mail deja el
usuario huérfano en la base SQL Server compartida (`localhost,14330`) y, con el índice único de CUIT,
contamina todas las corridas posteriores —un fallo que aparecería en un test que no tiene nada que ver
con este ticket—. El cleanup pasa a filtrar por `Id`, que no cambia nunca. La paralelización ya está
deshabilitada, así que no hay carrera entre clases de test.

**API contract**

- Método y ruta: `PUT /api/compradores/mi-cuenta`
- Request (`ActualizarCuentaRequest`, JSON): `Apellido` (string, requerido), `Nombre` (string,
  requerido), `Cuit` (string, requerido), `Mail` (string, requerido), `ContrasenaActual` (string,
  requerido — prueba de identidad, **no es un dato de la cuenta**: se verifica y se descarta, nunca
  se persiste ni se devuelve)
- Response 200 (`CuentaCompradorResponse`): `Apellido` (string), `Nombre` (string), `Cuit` (string),
  `Mail` (string) — el estado ya actualizado. Además, cabecera `Set-Cookie` con la cookie
  `bingocart_auth` reemitida
- Response 200: **nunca** incluye `ContrasenaActual` ni ningún derivado de ella
- Códigos de error: 400 `"DatosInvalidos"` (campo faltante o vacío), 400 `"CuitInvalido"` (longitud o
  dígito verificador), 403 `"ContrasenaIncorrecta"`, 409 `"MailEnUso"`, 409 `"CuitEnUso"`, 409
  `"PlazoModificacionVencido"`, 401, 429
- Auth: `[Authorize(Roles = "Comprador")]`. `compradorId` derivado exclusivamente del claim
  `NameIdentifier` (NFR-04); el cuerpo **no** lleva ningún identificador de usuario

**Input validation**

Delegada al Block 5 (los cuatro campos, con las reglas ya detalladas ahí), más las anotaciones de
`[Required]` en el request DTO para que un campo ausente se rechace con 400 en el binding, antes de
llegar al service.

**Error handling**

- `PlazoModificacionVencidoException` → 409 `"PlazoModificacionVencido"`. Se elige 409 y no 403
  porque el rechazo no es de autorización —el comprador tiene todo el derecho a editar su cuenta—
  sino de estado: la operación entra en conflicto con la situación actual de sus compras.
- `ContrasenaIncorrectaException` → **403** `"ContrasenaIncorrecta"`. Se elige 403 y no 401 porque la
  sesión es válida: lo que falta no es autenticación sino la prueba de identidad que esta operación
  concreta exige. Devolver 401 haría que un cliente razonable interpretara "la sesión venció" y
  cerrara sesión, que es exactamente lo contrario de lo que pasó.
- `MailEnUsoException` → 409 `"MailEnUso"`, **sin repetir la dirección** en el mensaje.
- `CuitEnUsoException` → 409 `"CuitEnUso"`, **sin repetir el CUIT** en el mensaje.
- `CuitInvalidoException` → 400 `"CuitInvalido"`, ya mapeada en el middleware; no requiere catch
  nuevo.
- Campo faltante o vacío → 400 `"DatosInvalidos"` por el mecanismo de `[Required]` ya vigente.
- Sin autenticación → 401; sin rol `Comprador` → 403. Por el pipeline de `[Authorize]`.
- Exceder el rate limit → 429.
- Todos los cuerpos de error mantienen el criterio del proyecto: mensaje de dominio pensado para ser
  público, sin stack traces ni detalles de infraestructura.

**Required tests**

- [ ] `CompradoresControllerTests.ActualizarCuenta_ConDatosValidos_Devuelve200ConElEstadoActualizado` —
      valida AC-06
- [ ] `CompradoresControllerTests.ActualizarCuenta_ConDatosValidos_ReemiteLaCookieDeSesion` — valida
      D-05
- [ ] `CompradoresControllerTests.ActualizarCuenta_ConSorteoInminente_Devuelve409PlazoModificacionVencido` —
      valida AC-07, sad path
- [ ] `CompradoresControllerTests.ActualizarCuenta_ConMailDeOtraCuenta_Devuelve409MailEnUso` — valida
      AC-08, sad path
- [ ] `CompradoresControllerTests.ActualizarCuenta_ConCuitDeOtraCuenta_Devuelve409CuitEnUso` — valida
      AC-11, sad path
- [ ] `CompradoresControllerTests.ActualizarCuenta_ConCuitDeLongitudInvalida_Devuelve400CuitInvalido` —
      valida AC-09, sad path
- [ ] `CompradoresControllerTests.ActualizarCuenta_ConDigitoVerificadorInvalido_Devuelve400CuitInvalido` —
      valida AC-09, sad path
- [ ] `CompradoresControllerTests.ActualizarCuenta_ConApellidoVacio_Devuelve400DatosInvalidos` — sad
      path de entrada inválida
- [ ] `CompradoresControllerTests.ActualizarCuenta_SinRolComprador_Devuelve403` — sad path de
      autorización
- [ ] `CompradoresControllerTests.ActualizarCuenta_SinAutenticar_Devuelve401` — sad path de
      autenticación
- [ ] `CompradoresControllerTests.ActualizarCuenta_ConContrasenaIncorrecta_Devuelve403SinModificarNada` —
      valida AC-16 y FR-13, sad path
- [ ] `CompradoresControllerTests.ActualizarCuenta_SinContrasena_Devuelve400DatosInvalidos` — sad
      path de campo faltante
- [ ] `CompradoresControllerTests.ActualizarCuenta_LaRespuestaNoIncluyeLaContrasena` — el 200 no
      devuelve la contraseña ni ningún derivado
- [ ] `CompradoresControllerTests.ActualizarCuenta_SuperandoElLimiteDeSolicitudes_Devuelve429` —
      valida NFR-02
- [ ] `CompradoresControllerTests` — tras un test que cambia el mail, el `DisposeAsync` corregido deja
      la base sin usuarios huérfanos, verificado consultando por `Id`

**Completion criterion**

El `PUT` devuelve 200 con los cuatro campos actualizados y una cookie nueva cuyo claim `Email`
coincide con el mail nuevo; los cinco caminos de rechazo devuelven su código y su mensaje propios; una
corrida completa de `BingoCart.Api.Tests` seguida de una segunda corrida pasa en verde las dos veces,
demostrando que el cleanup ya no contamina; una solicitud con la contraseña incorrecta devuelve 403 y
deja la fila de `AspNetUsers` idéntica; los 15 tests en verde.

---

## Final verification

- Los 13 FR y los 16 AC del PRD tienen cobertura, trazada en la tabla de Coverage y en los tests de
  cada bloque.
- La migración aplica y revierte limpiamente sobre una base con cartones preexistentes, dejando
  correlativos distintos de 1 a N por bingo.
- Suite backend completa en verde (Domain + Application + Infrastructure + Api), en **dos corridas
  consecutivas** — la segunda es la que demuestra que el cleanup corregido de `CompradoresControllerTests`
  no deja residuo.
- `dotnet format BingoCart.sln --verify-no-changes` limpio.
- `dotnet build -warnaserror:CS8019,IDE0005` sin warnings.
- Ningún endpoint, query string ni request DTO acepta el número correlativo como entrada.
- Ningún log de código productivo nuevo incluye PII del comprador ni `ex.Message`.
