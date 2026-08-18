# Spec FEAT-003: Crear bingo con generación de cartones

| Field | Value |
|-------|-------|
| Ticket | FEAT-003 |
| PRD | docs/daw/prd/prd-FEAT-003.md |
| Tier | FEATURE |
| Date | 2026-08-17 |
| Spec loops | 0 |

## Summary

Implementa `POST /api/bingos` (protegido): un organizador autenticado crea un bingo y el sistema
genera atómicamente sus cartones (10 números únicos entre 1-90 por CSPRNG, GUID único, sin
conjuntos repetidos dentro del bingo). Nuevo agregado de dominio `Bingo` + entidad `Carton`,
siguiendo el patrón exacto de `Organizador` (factory `Crear`, excepciones de dominio tipadas,
inmutabilidad). Es **backend-only** — el PRD no tiene ningún AC de UI, confirmado por el impact
scan (ninguna pantalla de creación en el frontend todavía).

## Coverage: PRD → blocks

| Requirement | Covered by |
|---|---|
| FR-01 | Block 1, Block 4 |
| FR-02 | Block 1, Block 4 |
| FR-03 | Block 2, Block 4 |
| FR-04 | Block 1, Block 4 |
| FR-05 | Block 2, Block 3 |
| FR-06 | Block 3, Block 4 |
| FR-07 | Block 1, Block 4 |
| NFR-01 | Strategy: generación 100% en memoria (Block 2, sin I/O por cartón) + un único `SaveChangesAsync`/`AddRange` (Block 3, el proveedor SqlServer de EF Core batchea el insert); Block 4 mide con `Stopwatch` el ciclo completo (generación + persistencia) creando un bingo de 5.000 cartones. |
| NFR-02 | Strategy: `ICartonNumberGenerator` (puerto en Application, Block 2) implementado exclusivamente con `System.Security.Cryptography.RandomNumberGenerator` en Infrastructure — sin precedente previo de aleatoriedad en el repo (confirmado por impact scan), 0 usos de `System.Random`, verificable por SAST. |

## Dependencies between blocks

Block 1 (dominio: `Bingo`/`Carton`, sin persistencia ni generación) no depende de nada — es la
base. Block 2 (generador CSPRNG) depende de Block 1 (usa el tipo `Carton`/sus invariantes de
`Numeros`). Block 3 (persistencia EF Core + `IBingoRepository`) depende de Block 1 (mapea las
entidades) — no depende de Block 2. Block 4 (Application + Api) depende de los tres anteriores
(orquesta dominio + generador + repositorio, y expone el endpoint). Orden: 1 → {2, 3} → 4 (Block 2
y Block 3 pueden implementarse en cualquier orden entre sí una vez cerrado Block 1, pero ambos
deben estar listos antes de Block 4).

**Decisiones cerradas en PLAN (no reabrir en CODE):**
- **Ruta del endpoint**: `POST /api/bingos` (controller nuevo `BingosController`, no anidado bajo
  `/api/organizadores`) — el recurso creado es un Bingo, su dueño se deriva del JWT, no de la URL.
- **`Bingo.Id` y `Carton.Id`**: `Guid` generados en el factory de dominio (`Guid.NewGuid()`), mismo
  patrón que `Organizador.Id`.
- **FK del organizador dueño**: `Bingo.OrganizadorId` apunta al mismo `Guid` que
  `ApplicationUser.Id` (confirmado por impact scan: no existe una tabla `Organizadores` separada,
  el agregado de dominio `Organizador` nunca se persiste como tal — el `NameIdentifier` del JWT es
  ese mismo id).
- **Representación de `Carton.Numeros`**: `IReadOnlyList<int>` en el dominio (10 enteros, orden
  ascendente canónico), persistido como una única columna string delimitada por comas (ej.
  `"3,12,45,67,68,71,80,85,88,90"`) vía un `ValueConverter` de EF Core — no una tabla hija
  `CartonNumero` normalizada. Justificación: con hasta 5.000 cartones por bingo, una tabla hija
  normalizada implicaría hasta 50.000 filas adicionales por creación (R-02 del PRD, ya señalado como
  riesgo de escala); una columna string permite `AddRange` de un solo nivel (5.000 filas, no
  50.000) y aun así soporta un índice único de base de datos sobre `(BingoId, NumerosSerializados)`
  como red de seguridad además de la validación en memoria (Block 2/3) — no se pierde ninguna
  garantía de integridad por esta elección.
- **Reloj**: `Bingo.Crear` recibe `ahoraUtc` como parámetro explícito (no consulta `TimeProvider`
  por su cuenta) — mantiene el dominio puro/sin I/O (AGENTS.md), Application (que sí tiene
  `TimeProvider` inyectado, mismo patrón que `OrganizadorService`) se lo pasa resuelto.
- **Rate limiting POR ORGANIZADOR**: SÍ se agrega, a diferencia de lo evaluado inicialmente en el
  borrador de este spec — hallazgo de `/daw-threat-modeling` (riesgo TM-01, ver
  `docs/daw/security/threat-FEAT-003.md`): el chequeo de "bingo activo" (FR-06) evita el abuso
  CONCURRENTE, pero no evita que un organizador fije `fechaSorteoUtc` apenas en el futuro (ej. +1
  minuto) y repita la creación —con su generación de hasta 5.000 cartones— cada vez que esa fecha
  vence, indefinidamente. Se agrega una política de `RateLimiter` nueva, particionada por
  `organizadorId` (no por IP, a diferencia de la política `"registro"` ya existente, porque este
  endpoint requiere autenticación): máximo 3 creaciones cada 5 minutos por organizador —
  suficientemente generoso para reintentos legítimos tras corregir un error de validación, pero
  acota el abuso sostenido del camino costoso.
- **Timestamp de creación** (`Bingo.FechaCreacionUtc`): agregado tras threat modeling (riesgo TM-02,
  Repudiation) — sin él, no queda registro de cuándo se creó un bingo, solo de cuándo es su sorteo.
  Ver Block 1/Block 3 para el campo agregado.

## Block 1 — Dominio: agregado `Bingo` y entidad `Carton`

**Files**
- `backend/BingoCart.Domain/Bingos/Bingo.cs` (new) — `sealed class Bingo`, mismo patrón que
  `Organizador.cs`: propiedades `{ get; private init; }`, constructor privado, factory estático
  `Crear(string nombreEvento, DateTime fechaSorteoUtc, int cantidadCartones, decimal
  costoPorCarton, Guid organizadorId, DateTime ahoraUtc)`. Valida en orden: `fechaSorteoUtc >
  ahoraUtc` (si no, `FechaSorteoInvalidaException`), `cantidadCartones > 5000`
  (`CantidadCartonesExcedeLimiteException`), `cantidadCartones <= 0`
  (`CantidadCartonesInvalidaException`), `costoPorCarton <= 0`
  (`CostoPorCartonInvalidoException`). `Id = Guid.NewGuid()` y `FechaCreacionUtc = ahoraUtc`
  asignados dentro del factory (mitigación TM-02 de threat modeling, Repudiation — sin este campo
  no queda registro de cuándo se creó el bingo). Este
  factory NO genera los cartones (eso es Block 2/4) — solo construye el agregado `Bingo` en sí.
- `backend/BingoCart.Domain/Bingos/Carton.cs` (new) — `sealed class Carton`, factory `Crear(Guid
  bingoId, IReadOnlyList<int> numeros)`: valida `numeros.Count == 10` y todos distintos entre sí
  (invariante propia del cartón, independiente de la unicidad ENTRE cartones que valida Block 2) —
  lanza `NumerosCartonInvalidosException` si no se cumple. Normaliza `Numeros` en orden ascendente
  al construir (para que la representación canónica sea determinística). `Id = Guid.NewGuid()`
  dentro del factory.
- `backend/BingoCart.Domain/Bingos/Exceptions/FechaSorteoInvalidaException.cs` (new) — hereda
  `DomainException`, mensaje fijo: la fecha de sorteo debe ser futura.
- `backend/BingoCart.Domain/Bingos/Exceptions/CantidadCartonesExcedeLimiteException.cs` (new) —
  hereda `DomainException`, mensaje indicando el límite de 5.000 (AC-02, mensaje específico
  distinto del resto).
- `backend/BingoCart.Domain/Bingos/Exceptions/CantidadCartonesInvalidaException.cs` (new) — hereda
  `DomainException`, mensaje fijo: la cantidad debe ser mayor a cero.
- `backend/BingoCart.Domain/Bingos/Exceptions/CostoPorCartonInvalidoException.cs` (new) — hereda
  `DomainException`, mensaje fijo: el costo debe ser mayor a cero.
- `backend/BingoCart.Domain/Bingos/Exceptions/NumerosCartonInvalidosException.cs` (new) — hereda
  `DomainException`, mensaje fijo: un cartón debe tener exactamente 10 números distintos entre 1 y
  90.
- `backend/BingoCart.Domain/Bingos/Exceptions/BingoActivoExistenteException.cs` (new) — hereda
  `DomainException` (lanzada por Application en Block 4, no por este bloque, pero vive en Domain
  por consistencia con el resto de excepciones de negocio), mensaje fijo: el organizador ya tiene
  un bingo activo.

**Logic**
Dos agregados/entidades inmutables, sin I/O, sin dependencias externas — mismo criterio que
`Organizador.cs` (AGENTS.md: "los métodos de dominio no deben tener side effects"). `Bingo.Crear`
no conoce cartones ni generación; `Carton.Crear` no conoce otros cartones del mismo bingo (esa
comparación cruzada es responsabilidad de Block 2, que sí ve el conjunto completo que está
generando).

**API contract**
N/A — este bloque no expone ningún endpoint.

**Data model**
N/A a nivel de persistencia (eso es Block 3) — a nivel de dominio: `Bingo { Id: Guid,
OrganizadorId: Guid, NombreEvento: string, FechaSorteoUtc: DateTime, FechaCreacionUtc: DateTime,
CantidadCartones: int, CostoPorCarton: decimal }`. `Carton { Id: Guid, BingoId: Guid, Numeros:
IReadOnlyList<int> (10 enteros, 1-90, ascendente) }`.

**Input validation**
Validado por el factory de `Bingo.Crear`: `FechaSorteoUtc` futura, `CantidadCartones` en (0,
5000], `CostoPorCarton` > 0. `NombreEvento` NO se valida en el factory — corrección tras revisión
de `daw-arch-auditor` en PLAN: el precedente real (`Organizador.Crear`) tampoco valida
`NombreOrganizacion` vacío en Domain, esa validación vive exclusivamente en el DTO
(`RegistrarOrganizadorRequest` vía `[Required]`) — `CrearBingoRequest` sigue el mismo criterio
(Block 4). `Carton.Crear` valida exactamente 10 enteros distintos entre 1 y 90.

**Error handling**
(`NombreEvento` vacío NO se maneja acá — no es un error de este bloque, ver Block 4: es una
validación de modelo sobre el DTO, `Bingo.Crear` ni siquiera recibe la oportunidad de rechazarlo
distinto a como ya llega.)
- `FechaSorteoUtc` no futura → `FechaSorteoInvalidaException` (AC-06).
- `CantidadCartones` > 5000 → `CantidadCartonesExcedeLimiteException` (AC-02).
- `CantidadCartones` ≤ 0 → `CantidadCartonesInvalidaException` (AC-06).
- `CostoPorCarton` ≤ 0 → `CostoPorCartonInvalidoException` (AC-06).
- `Carton` con `Numeros.Count != 10`, con duplicados internos, o con algún número fuera de 1-90 →
  `NumerosCartonInvalidosException` — corrección tras revisión de `daw-arch-auditor` en CODE: el
  rango SÍ lo valida este factory (no solo Block 2), para que sea estructuralmente imposible
  construir un `Carton` inválido; el borrador previo de esta lista era inconsistente con "Files" e
  "Input validation" de este mismo bloque, que ya lo exigían.

**Required tests**
- [ ] `Bingo.Crear` con datos válidos → bingo creado con `Id` no vacío, campos correctos — valida
  AC-01 (parte de dominio).
- [ ] `Bingo.Crear` con `fechaSorteoUtc` en el pasado → `FechaSorteoInvalidaException` — valida
  AC-06.
- [ ] `Bingo.Crear` con `cantidadCartones` = 5001 → `CantidadCartonesExcedeLimiteException` —
  valida AC-02.
- [ ] `Bingo.Crear` con `cantidadCartones` = 0 → `CantidadCartonesInvalidaException` — valida
  AC-06.
- [ ] `Bingo.Crear` con `costoPorCarton` = 0 → `CostoPorCartonInvalidoException` — valida AC-06.
- [ ] `Carton.Crear` con 10 números válidos → cartón creado con `Numeros` en orden ascendente.
- [ ] `Carton.Crear` con un número repetido dentro de los 10 → `NumerosCartonInvalidosException`.
- [ ] `Carton.Crear` con menos de 10 números → `NumerosCartonInvalidosException`.
- [ ] `Carton.Crear` con un número mayor a 90 → `NumerosCartonInvalidosException`.
- [ ] `Carton.Crear` con un número menor a 1 → `NumerosCartonInvalidosException`.

**Completion criterion**
Los 10 tests pasan; `Bingo`/`Carton` son inmutables, sin I/O, y solo se pueden construir en un
estado válido.

## Block 2 — Generación de cartones (CSPRNG)

**Files**
- `backend/BingoCart.Application/Bingos/ICartonNumberGenerator.cs` (new) — puerto: `IReadOnlyList<
  IReadOnlyList<int>> GenerarConjuntosUnicos(int cantidad)` — devuelve `cantidad` conjuntos de 10
  números (1-90) cada uno, garantizados distintos ENTRE SÍ (no valida nada de `Carton`, eso lo hace
  Block 1 cuando Block 4 arme los `Carton` con estos conjuntos).
- `backend/BingoCart.Infrastructure/Bingos/CartonNumberGenerator.cs` (new) — implementa el puerto
  usando exclusivamente `System.Security.Cryptography.RandomNumberGenerator`
  (`RandomNumberGenerator.GetInt32(1, 91)` o un shuffle parcial Fisher-Yates de `[1..90]` tomando
  los primeros 10 — a criterio del implementador, cualquiera de las dos formas usa CSPRNG
  correctamente). Por cada conjunto generado, lo normaliza en orden ascendente y lo compara (como
  string canónico, ej. `string.Join(",", numeros)`) contra un `HashSet<string>` de los conjuntos ya
  generados en esta llamada — si colisiona (astronómicamente improbable: hay
  C(90,10) ≈ 5,72 × 10¹² combinaciones posibles), regenera solo ese conjunto (hasta 10 reintentos;
  si los 10 fallan, lanza `InvalidOperationException` — un caso que en la práctica no debería
  ocurrir nunca con cantidades ≤ 5.000).

**Logic**
Generación 100% en memoria — sin acceso a base de datos por cartón (NFR-01: la comparación de
unicidad es un lookup en un `HashSet<string>` en memoria, O(1) amortizado). El puerto vive en
Application (mismo patrón que `IJwtTokenService` en FEAT-001b), la implementación con el detalle
criptográfico vive en Infrastructure — Application nunca importa `System.Security.Cryptography`
directamente.

**API contract**
N/A.

**Data model**
N/A.

**Input validation**
`cantidad` debe ser > 0 (ya garantizado por el momento en que Block 4 llama a este puerto, después
de que `Bingo.Crear` ya validó `CantidadCartones` — este bloque no revalida, confía en el
contrato del llamador).

**Error handling**
N/A como error de negocio testeable — el mecanismo de reintento ante colisión (ver Logic) es un
detalle de implementación defensivo para un escenario con probabilidad prácticamente nula (1 en
~5,7 × 10¹²), no una condición de error que el spec exija cubrir con un test dedicado: simularla
requeriría inyectar un generador de números falso, una complejidad no justificada para un caso que
en la práctica nunca ocurre. Si los 10 reintentos fallaran igual, el `InvalidOperationException`
resultante se propaga sin capturar (ningún catch silencioso), quedando como un 500 no manejado —
comportamiento aceptado explícitamente, no un gap.

**Required tests**
- [ ] `GenerarConjuntosUnicos(1)` → devuelve 1 conjunto de exactamente 10 números, todos entre 1 y
  90, todos distintos entre sí — valida FR-03/NFR-02.
- [ ] `GenerarConjuntosUnicos(100)` → los 100 conjuntos son todos distintos entre sí (comparación
  por conjunto ordenado) — valida FR-05.
- [ ] `GenerarConjuntosUnicos(5000)` → completa sin excepción y los 5.000 conjuntos son todos
  distintos entre sí — valida FR-05 a la escala máxima permitida (AC-04 a nivel de generador).
- [ ] Inspección de código (no un test automatizado, una verificación explícita en la revisión del
  bloque): la implementación usa exclusivamente `RandomNumberGenerator`, cero apariciones de `new
  Random(` o `System.Random` — valida NFR-02, reforzado además por SAST en CODE.

**Completion criterion**
Los 3 tests automatizados pasan; la generación de 5.000 conjuntos no produce ninguna colisión real
en la práctica (los reintentos, si ocurren, son transparentes) y usa exclusivamente CSPRNG.

## Block 3 — Persistencia: EF Core + `IBingoRepository`

**Files**
- `backend/BingoCart.Infrastructure/Data/AppDbContext.cs` (modified) — agrega `DbSet<Bingo>
  Bingos` y `DbSet<Carton> Cartones`; en `OnModelCreating`, Fluent API (mismo patrón que la
  configuración existente de `ApplicationUser`): `Bingo` con `HasKey(Id)`,
  `Property(NombreEvento).HasMaxLength(200).IsRequired()`, índice `HasIndex(OrganizadorId)` (no
  único — un organizador puede tener bingos históricos, solo uno vigente a la vez, validado en
  Application). `Carton` con `HasKey(Id)`, `HasOne<Bingo>().WithMany().HasForeignKey(BingoId)`,
  `Property(Numeros).HasConversion(v => string.Join(",", v), v =>
  v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList())` (`ValueConverter`
  para la representación canónica string, decisión de PLAN), y
  `HasIndex(BingoId, NumerosSerializados).IsUnique()` — el nombre de la columna generada por el
  converter (`NumerosSerializados` o el que EF Core infiera; ajustar el índice al nombre real de
  columna que resulte).
- `backend/BingoCart.Infrastructure/Data/Migrations/<timestamp>_AddBingosYCartones.cs` (new,
  generada con `dotnet ef migrations add AddBingosYCartones` — mismo mecanismo que la migración
  `InitialCreate` existente, se aplica sola al arrancar vía `MigrateAsync` en `Program.cs`, sin
  tocar ese archivo).
- `backend/BingoCart.Application/Bingos/IBingoRepository.cs` (new) — puerto: `Task<bool>
  TieneBingoActivoAsync(Guid organizadorId, DateTime ahoraUtc)` (existe un bingo de ese organizador
  con `FechaSorteoUtc > ahoraUtc`), `Task CrearAsync(Bingo bingo, IReadOnlyList<Carton> cartones)`
  (persiste el bingo y sus cartones en una sola operación).
- `backend/BingoCart.Infrastructure/Bingos/BingoRepository.cs` (new) — implementa el puerto contra
  `AppDbContext`: `TieneBingoActivoAsync` es un `AnyAsync` indexado; `CrearAsync` hace
  `_context.Bingos.Add(bingo)` + `_context.Cartones.AddRange(cartones)` + un único
  `SaveChangesAsync()` (transacción implícita de EF Core sobre un solo `SaveChanges`, no hace falta
  una transacción explícita adicional).

**Logic**
Capa de infraestructura pura — sin lógica de negocio, delega toda decisión (si un bingo está
"activo", qué cartones generar) a quien la invoque (Block 4). El índice único
`(BingoId, NumerosSerializados)` es la red de seguridad a nivel de base de datos para FR-05,
además de la validación en memoria de Block 2 — si por algún motivo ambas fallaran, el `INSERT`
fallaría con `DbUpdateException` en vez de persistir un duplicado silenciosamente.

**API contract**
N/A.

**Data model**
Tabla `Bingos`: `Id (PK, uniqueidentifier)`, `OrganizadorId (uniqueidentifier, FK lógica a
AspNetUsers.Id, indexada, no única)`, `NombreEvento (nvarchar(200), not null)`, `FechaSorteoUtc
(datetime2, not null)`, `FechaCreacionUtc (datetime2, not null — mitigación TM-02, threat model)`,
`CantidadCartones (int, not null)`, `CostoPorCarton (decimal(10,2), not null)`.
Tabla `Cartones`: `Id (PK, uniqueidentifier)`, `BingoId (uniqueidentifier, FK a Bingos.Id, not
null)`, `NumerosSerializados (nvarchar(60), not null)` — índice único compuesto
`(BingoId, NumerosSerializados)`.

**Input validation**
N/A — esta capa recibe agregados de dominio ya validados (Block 1), no input crudo.

**Error handling**
- Violación del índice único `(BingoId, NumerosSerializados)` en `CrearAsync` → `DbUpdateException`
  propagada sin capturar en este bloque (es un caso defensivo que, si ocurre, indica un bug en
  Block 2 — Block 4 decide si la traduce a una respuesta HTTP específica o la deja como 500;
  documentado como comportamiento esperado, no oculto).

**Required tests**
- [ ] `TieneBingoActivoAsync` con un bingo de `FechaSorteoUtc` futura para ese organizador → `true`.
- [ ] `TieneBingoActivoAsync` con un bingo de `FechaSorteoUtc` pasada para ese organizador → `false`.
- [ ] `TieneBingoActivoAsync` sin ningún bingo para ese organizador → `false`.
- [ ] `CrearAsync` con un bingo + N cartones válidos → los N cartones quedan persistidos y
  recuperables con sus 10 números correctos.
- [ ] Insertar dos cartones con el mismo `BingoId` y el mismo conjunto de números (vía acceso
  directo a `AppDbContext`, no a través de `CrearAsync`, para forzar el escenario) →
  `DbUpdateException` por violación del índice único — valida la red de seguridad de FR-05 a nivel
  de esquema.

**Completion criterion**
Los 5 tests pasan contra SQL Server real (integración); la migración se genera y aplica sin
intervención manual adicional al mecanismo ya existente.

## Block 4 — Application + Api: orquestación y endpoint

**Files**
- `backend/BingoCart.Application/Bingos/Dtos/CrearBingoRequest.cs` (new) — `record
  CrearBingoRequest([Required, MaxLength(200)] string NombreEvento, [Required] DateTime
  FechaSorteoUtc, [Required] int CantidadCartones, [Required] decimal CostoPorCarton)` —
  DataAnnotations en el constructor primario, mismo patrón que
  `RegistrarOrganizadorRequest`/`LoginOrganizadorRequest`. **Sin `[Range(...)]`** en
  `CantidadCartones` ni `CostoPorCarton` — corrección tras revisión de `daw-arch-auditor` en PLAN:
  con `[ApiController]`, un `Range` ahí dispararía el `InvalidModelStateResponseFactory` (400
  `DatosInvalidos`) ANTES de que `Bingo.Crear` se ejecute siquiera, haciendo inalcanzables
  `CantidadCartonesExcedeLimiteException`/`CantidadCartonesInvalidaException`/
  `CostoPorCartonInvalidoException` (Block 1) — exactamente el mismo criterio que ya sigue
  `RegistrarOrganizadorRequest` (Cuit/Telefono/Password sin DataAnnotations propias, porque cada
  uno tiene su excepción de dominio dedicada). `[Required]` es la única validación de modelo:
  cubre nulls/ausencia del campo, sin competir con los rangos que valida Domain.
- `backend/BingoCart.Application/Bingos/Dtos/BingoCreadoResponse.cs` (new) — `record
  BingoCreadoResponse(Guid Id, string NombreEvento, DateTime FechaSorteoUtc, int CantidadCartones,
  decimal CostoPorCarton)` — confirmación de creación, SIN los cartones individuales (no hay AC que
  exija devolverlos, y 5.000 cartones en un solo JSON de respuesta es impracticable — quedan
  disponibles para consulta en un ticket futuro, "Mis bingos").
- `backend/BingoCart.Application/Bingos/IBingoService.cs` (new) — puerto: `Task<
  BingoCreadoResponse> CrearAsync(CrearBingoRequest request, Guid organizadorId)`.
- `backend/BingoCart.Application/Bingos/BingoService.cs` (new) — implementa `CrearAsync`:
  1. `Bingo.Crear(request.NombreEvento, request.FechaSorteoUtc, request.CantidadCartones,
     request.CostoPorCarton, organizadorId, _timeProvider.GetUtcNow().UtcDateTime)` — primero,
     porque es gratis y sin I/O (fail-fast antes de tocar la base de datos); puede lanzar
     `FechaSorteoInvalidaException`/`CantidadCartonesExcedeLimiteException`/
     `CantidadCartonesInvalidaException`/`CostoPorCartonInvalidoException` — corrección de orden
     tras revisión de `daw-arch-auditor` en PLAN respecto al borrador original (que consultaba la
     base de datos primero).
  2. `await _bingoRepository.TieneBingoActivoAsync(organizadorId, _timeProvider.GetUtcNow()
     .UtcDateTime)` → si `true`, lanza `BingoActivoExistenteException` (segundo: I/O barato, pero
     todavía antes del paso costoso — sigue satisfaciendo la mitigación de threat modeling de no
     generar cartones si el organizador ya tiene un bingo activo).
  3. `_cartonNumberGenerator.GenerarConjuntosUnicos(request.CantidadCartones)`.
  4. Arma la lista de `Carton.Crear(bingo.Id, conjunto)` para cada conjunto generado.
  5. `await _bingoRepository.CrearAsync(bingo, cartones)`.
  6. Devuelve `BingoCreadoResponse` con los datos del bingo creado.
  Constructor recibe `IBingoRepository`, `ICartonNumberGenerator`, `TimeProvider` (mismo patrón que
  `OrganizadorService`, sin necesitar `IOptions<T>` de ningún tipo — a diferencia del caso de
  `JwtSettings` en FEAT-001b, acá no hay ninguna dependencia de Infrastructure que evitar).
- `backend/BingoCart.Api/Controllers/BingosController.cs` (new) — `[Authorize(AuthenticationSchemes
  = JwtBearerDefaults.AuthenticationScheme)] [Route("api/bingos")] [ApiController]`. Un único
  método: `[HttpPost] [EnableRateLimiting("bingos")] public async Task<ActionResult<
  BingoCreadoResponse>> Crear([FromBody] CrearBingoRequest request)` — lee
  `Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!)` (mismo claim confirmado por el
  impact scan que usa `JwtTokenService` al emitir el token), llama a
  `IBingoService.CrearAsync(request, organizadorId)`, devuelve `CreatedAtAction`/`StatusCode(201)`
  con el response. Delega 100% a Application, sin lógica de negocio (mismo patrón que
  `OrganizadoresController.Login`/`Registrar`).
- `backend/BingoCart.Api/Middleware/ExceptionHandlingMiddleware.cs` (modified) — agrega los 5
  mapeos nuevos: `FechaSorteoInvalidaException` → 400, `CantidadCartonesExcedeLimiteException` →
  400, `CantidadCartonesInvalidaException` → 400, `CostoPorCartonInvalidoException` → 400,
  `BingoActivoExistenteException` → 409 (mismo código que `MailYaRegistradoException`, conflicto
  con estado existente).
- `backend/BingoCart.Api/Program.cs` (modified) — registra
  `builder.Services.AddScoped<IBingoService, BingoService>();`,
  `builder.Services.AddScoped<IBingoRepository, BingoRepository>();`,
  `builder.Services.AddSingleton<ICartonNumberGenerator, CartonNumberGenerator>();` (singleton
  porque no tiene estado ni depende de nada con lifetime más corto — mismo criterio que
  `JwtTokenService`). Además, en `AddRateLimiter` (donde ya vive la política `"registro"`), agrega
  la política `"bingos"`: `RateLimitPartition.GetFixedWindowLimiter(partitionKey:
  httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown", factory: _ => new
  FixedWindowRateLimiterOptions { PermitLimit = 3, Window = TimeSpan.FromMinutes(5) })` —
  particionada por `organizadorId` (usuario autenticado), no por IP como `"registro"` (endpoint
  público) — mitigación TM-01 de threat modeling.

**API contract**
- Method + path: `POST /api/bingos`
- Request body: `{ "nombreEvento": "string (≤200 chars)", "fechaSorteoUtc": "string (ISO 8601
  UTC)", "cantidadCartones": "int (1-5000)", "costoPorCarton": "decimal (>0)" }`
- Response 201: `{ "id": "guid", "nombreEvento": "string", "fechaSorteoUtc": "string",
  "cantidadCartones": "int", "costoPorCarton": "decimal" }`
- Response 400: datos inválidos — modelo (`{ "error": "DatosInvalidos", "message": "..." }`, vía
  `InvalidModelStateResponseFactory` ya existente) o dominio (`{ "error":
  "FechaSorteoInvalida"|"CantidadCartonesExcedeLimite"|"CantidadCartonesInvalida"|
  "CostoPorCartonInvalido", "message": "..." }`, mismo formato que las excepciones de dominio ya
  mapeadas de `OrganizadoresController`).
- Response 401: sin autenticación (cookie `bingocart_auth` ausente/inválida/expirada — pipeline ya
  existente de FEAT-001b, sin código adicional).
- Response 409: `{ "error": "BingoActivoExistente", "message": "..." }` — el organizador ya tiene
  un bingo con fecha de sorteo vigente.
- Response 429: demasiadas creaciones en la ventana de tiempo (más de 3 en 5 minutos para el mismo
  organizador) — manejado automáticamente por `AddRateLimiter`/`[EnableRateLimiting("bingos")]`,
  sin código adicional (mismo mecanismo ya usado por `"registro"`), mitigación TM-01.
- Auth: JWT Bearer vía cookie httpOnly (`[Authorize]`), mismo mecanismo que `GET /api/organizadores/
  perfil`.

**Input validation**
Ver `CrearBingoRequest` en Files — validación de modelo (`MaxLength`, `Required`, SIN `Range`, ver
decisión de PLAN) como primera barrera, validación de dominio (Block 1) como fuente de verdad.

**Error handling**
Ver los 5 mapeos de excepción en Files (`ExceptionHandlingMiddleware`) — cada uno con su código
HTTP y mensaje específico, sin catch silencioso, mismo mecanismo ya usado por
`OrganizadoresController`.

**Required tests**
- [ ] `BingoServiceTests` (unit, mocks de `IBingoRepository`/`ICartonNumberGenerator`): creación
  con datos válidos → `BingoCreadoResponse` correcto, `CrearAsync` del repositorio invocado con el
  bingo y la cantidad correcta de cartones — valida AC-01 (orquestación).
- [ ] `BingoServiceTests`: `TieneBingoActivoAsync` devuelve `true` → lanza
  `BingoActivoExistenteException` SIN haber llamado a `ICartonNumberGenerator` (verifica que el
  chequeo barato ocurre antes de la generación costosa — parte de la mitigación de threat modeling)
  — valida AC-05.
- [ ] `BingosControllerTests` (integración, `WebApplicationFactory` + SQL Server real, login real +
  cliente HTTPS dedicado, mismo patrón que `Perfil_ConCookieDeLoginReal_...`): `POST /api/bingos`
  sin autenticación → 401.
- [ ] `BingosControllerTests`: `POST /api/bingos` con datos válidos (100 cartones) → 201; consulta
  directa a la BD confirma 100 cartones persistidos, cada uno con 10 números entre 1-90, y que
  `COUNT(DISTINCT Id) == 100` — valida AC-01 end-to-end, y AC-03 (FR-04, GUIDs únicos) de forma
  explícita: `Id` es la primary key de `Cartones`, así que 100 filas insertadas sin
  `DbUpdateException` ya prueba que los 100 GUIDs son distintos (una colisión habría violado la
  PK), pero se deja la aserción explícita para que la traceability de AC-03 sea legible en el test,
  no solo una consecuencia implícita del esquema.
- [ ] `BingosControllerTests`: `POST /api/bingos` con `cantidadCartones` = 5001 → 400
  `CantidadCartonesExcedeLimite` — valida AC-02 end-to-end.
- [ ] `BingosControllerTests`: `POST /api/bingos` con `fechaSorteoUtc` pasada → 400
  `FechaSorteoInvalida` — valida AC-06 end-to-end. Representativo, NO exhaustivo a propósito:
  `CantidadCartonesInvalida` y `CostoPorCartonInvalido` (los otros 2 casos del bucket AC-06) ya
  tienen su propio test dedicado a nivel de dominio en Block 1 — repetirlos acá como test de
  integración solo volvería a probar el mismo mecanismo de mapeo de `ExceptionHandlingMiddleware`
  que este test y el de `CantidadCartonesExcedeLimite` ya cubren, sin agregar cobertura real.
- [ ] `BingosControllerTests`: creado un bingo con sorteo vigente, un segundo `POST /api/bingos`
  del mismo organizador → 409 `BingoActivoExistente` — valida AC-05 end-to-end.
- [ ] `BingosControllerTests`: `POST /api/bingos` con `cantidadCartones` = 5000, midiendo con
  `Stopwatch` que la respuesta completa (generación + persistencia) toma menos de 10 segundos —
  valida NFR-01 end-to-end (AC-01 a la escala máxima).
- [ ] `BingosControllerTests`: sobre el bingo de 5.000 cartones del test anterior, consulta directa
  a la BD confirma que no hay dos cartones con el mismo conjunto de 10 números — valida AC-04
  end-to-end, a la escala máxima permitida.
- [ ] `BingosControllerTests`: un organizador que ya agotó sus 3 intentos permitidos en la ventana
  de 5 minutos (3 requests con datos inválidos, ej. cantidad excedida, para no chocar con la regla
  de bingo activo) → el 4° request devuelve 429 — valida la mitigación TM-01 de threat modeling.

**Completion criterion**
Los 10 tests pasan; un bingo real creado vía HTTP contra el stack contenedorizado persiste
exactamente la cantidad de cartones solicitada, cada uno válido y único, en menos de 10 segundos
para el caso de 5.000; un segundo intento del mismo organizador con un bingo vigente es rechazado
sin haber generado ningún cartón nuevo; un organizador que reintenta más de 3 veces en 5 minutos es
rechazado con 429 antes de llegar a `BingoService`.

## Final verification

`dotnet build`/`dotnet test` sobre `backend/BingoCart.sln` en verde, incluyendo los 28 tests
automatizados nuevos de los Blocks 1-4 (10+3+5+10, aproximado — el número exacto puede variar
levemente en CODE sin que eso invalide la cobertura). Un bingo creado vía `POST /api/bingos` con
hasta 5.000 cartones queda persistido en menos de 10 segundos, sin dos cartones con el mismo
conjunto de números, cada uno con GUID único, usando exclusivamente CSPRNG (verificado también por
SAST). Un organizador no puede
crear un segundo bingo mientras tenga uno con sorteo vigente. Ningún frontend se toca en este
ticket (confirmado backend-only por el PRD y el impact scan).
