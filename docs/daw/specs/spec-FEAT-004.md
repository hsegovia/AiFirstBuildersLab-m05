# Spec FEAT-004: Listar bingos propios del organizador

| Field | Value |
|-------|-------|
| Ticket | FEAT-004 |
| PRD | docs/daw/prd/prd-FEAT-004.md |
| Tier | FEATURE |
| Date | 2026-08-18 |
| Spec loops | 0 |

## Summary

Implementa `GET /api/bingos` (protegido): un organizador autenticado lista los bingos que él mismo
creó, paginados (page/pageSize, máximo 100 por página) y ordenados por fecha de creación
descendente. Reutiliza el agregado `Bingo`, el repositorio `IBingoRepository`/`BingoRepository` y el
índice `(OrganizadorId)`, todos de FEAT-003 — sin migraciones nuevas. **Backend-only**, igual que
FEAT-003 (el PRD no tiene ningún AC de UI, confirmado por el impact scan: ninguna pantalla "Mis
bingos" en el frontend todavía).

## Coverage: PRD → blocks

| Requirement | Covered by |
|---|---|
| FR-01 | Block 1, Block 2 |
| FR-02 | Block 2 |
| FR-03 | Block 1 |
| FR-04 | Block 1, Block 2 |
| FR-05 | Block 1, Block 2 |
| NFR-01 | Strategy: consulta contra el índice `(OrganizadorId)` ya existente (FEAT-003, Block 3), `Skip`/`Take` a nivel de SQL Server (no en memoria) — sin traer más filas que las de la página solicitada. |
| NFR-02 | Strategy: `organizadorId` derivado exclusivamente del claim JWT en el controller (Block 2), nunca de un parámetro de la request; test dedicado de aislamiento cross-organizador (AC-04) en ambos niveles (Infrastructure y Api). |

## Dependencies between blocks

Block 1 (repositorio: `ListarPorOrganizadorAsync`) no depende de nada nuevo — reutiliza `Bingo`
(Domain, FEAT-003) y `AppDbContext` (Infrastructure, FEAT-003) tal cual existen. Block 2
(Application + Api) depende de Block 1 (consume el método del repositorio). Orden: 1 → 2.

**Decisiones cerradas en PLAN (no reabrir en CODE):**

- **Sin endpoint anidado bajo `/api/organizadores`**: `GET /api/bingos` vive en el mismo
  `BingosController` que `POST /api/bingos` (FEAT-003) — mismo recurso, mismo controller, coherente
  con la decisión ya cerrada en FEAT-003 de que el dueño se deriva del JWT, no de la URL.
- **Retorno del repositorio — `record` dedicado, no tupla**: `IBingoRepository
  .ListarPorOrganizadorAsync` devuelve `Task<BingosPaginados>`, un `record` nuevo en
  `Application/Bingos/` — hallazgo de `daw-arch-auditor` en PLAN: este codebase usa siempre un
  `record` dedicado para retornos correlacionados desde un puerto de Application (precedente:
  `ResultadoAutenticacion`, `TokenGenerado`), nunca una tupla `(T, T)` en una firma pública. Una
  tupla acá habría introducido la primera inconsistencia de estilo en un puerto de Application, la
  capa donde el precedente es más fuerte.
- **Reutilización de `BingoCreadoResponse` en el listado, sin renombrar**: `BingoListadoResponse`
  (Block 2) usa `BingoCreadoResponse` (FEAT-003) tal cual para cada ítem — mismos 5 campos
  exactos (Id, NombreEvento, FechaSorteoUtc, CantidadCartones, CostoPorCarton). `daw-arch-auditor`
  señaló en PLAN que el XML doc de `BingoCreadoResponse` lo describe como "confirmación de
  creación" (atado semánticamente al 201 de `POST`), pero **se decide reutilizarlo igual, sin
  renombrar**: el PRD de este ticket confirma en su sección "Out of Scope" que el listado no
  necesita datos de ventas/estado que sí podrían diferenciar ambos casos de uso pronto, y el propio
  XML doc de `BingoCreadoResponse` ya anticipaba este uso exacto ("disponibles para consulta en un
  ticket futuro, 'Mis bingos'"). Si en un ticket futuro los dos casos de uso divergen (el de
  creación necesita un campo que el de listado no debe tener, o viceversa), se separan en ese
  momento — no antes (YAGNI). Renombrar ahora tocaría archivos de FEAT-003 ya mergeado sin necesidad
  real.
- **`ListarBingosQuery` con `[Range]`, a diferencia de `CrearBingoRequest`**: `CrearBingoRequest`
  (FEAT-003) evita `[Range]` a propósito porque ahí competiría con las excepciones de dominio de
  `Bingo.Crear`. Acá no hay ninguna invariante de `Bingo` que proteger — la paginación no es una
  regla de negocio del agregado — así que `[Range(1, int.MaxValue)]` en `Page`/`PageSize` y el 400
  automático de `InvalidModelStateResponseFactory` (ya registrado en `Program.cs`) es exactamente lo
  que pide AC-07 del PRD, sin duplicar ese mecanismo a mano.
- **`ListarBingosQuery` como record posicional**: mismo estilo sintáctico que `CrearBingoRequest`
  (DataAnnotations en el constructor primario, con valores default `Page = 1`, `PageSize = 20`). El
  binder de tipos complejos de ASP.NET Core 8 soporta binding por constructor para records desde
  `[FromQuery]` — no hace falta un constructor sin parámetros ni propiedades mutables. Verificado
  explícitamente por un test de integración en Block 2 (binding real desde query string, no un
  supuesto).
- **Sin rate limiting en `GET /api/bingos`**: a diferencia de `POST /api/bingos` (mitigación TM-01
  de FEAT-003, protege contra el costo de generar hasta 5.000 cartones), este GET no tiene un costo
  análogo — el resultado está acotado por la cantidad real de bingos del organizador y por el
  `pageSize` clampeado a 100. Mismo criterio que `GET /api/organizadores/perfil`, que tampoco tiene
  `[EnableRateLimiting]`.
- **Clamp de `pageSize` en Application, no solo `[Range]` en el DTO**: mitigación R-02 del threat
  model (`docs/daw/security/threat-FEAT-004.md`) — `BingoService.ListarPropiosAsync` limita
  `pageSize` a 100 como defensa en profundidad, sin depender únicamente de la validación de modelo.

## Block 1 — Infraestructura: listado paginado en el repositorio

**Files**
- `backend/BingoCart.Application/Bingos/BingosPaginados.cs` (new) — `sealed record
  BingosPaginados(IReadOnlyList<Bingo> Bingos, int Total)`, mismo patrón que
  `ResultadoAutenticacion`/`TokenGenerado` (decisión de PLAN, ver arriba).
- `backend/BingoCart.Application/Bingos/IBingoRepository.cs` (modified) — agrega al puerto:
  `Task<BingosPaginados> ListarPorOrganizadorAsync(Guid organizadorId, int page, int pageSize)`.
- `backend/BingoCart.Infrastructure/Bingos/BingoRepository.cs` (modified) — implementa
  `ListarPorOrganizadorAsync`: `_context.Bingos.Where(b => b.OrganizadorId == organizadorId)`,
  `CountAsync()` para el total, `.OrderByDescending(b => b.FechaCreacionUtc)
  .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync()` para la página, devuelve `new
  BingosPaginados(bingos, total)`.

**Logic**
Capa de infraestructura pura — sin decisiones de negocio (qué es una página "válida", cuál es el
máximo de `pageSize`): eso lo decide Application (Block 2). Este bloque solo traduce
`(organizadorId, page, pageSize)` a una consulta EF Core parametrizada sobre el índice
`(OrganizadorId)` ya existente (FEAT-003, Block 3) — sin migraciones nuevas.

**API contract**
N/A — este bloque no expone ningún endpoint.

**Data model**
N/A — sin cambios de esquema. Reutiliza la tabla `Bingos` y su índice `(OrganizadorId)` tal cual
existen desde FEAT-003.

**Input validation**
`page`/`pageSize` ya llegan validados por el momento en que Application invoca este método (Block
2) — este bloque no revalida, confía en el contrato del llamador (mismo criterio que
`CartonNumberGenerator.GenerarConjuntosUnicos` en FEAT-003).

**Error handling**
N/A — sin excepciones de negocio nuevas; una consulta EF Core fallida se propaga sin capturar
(ningún catch silencioso).

**Required tests**
- [ ] `ListarPorOrganizadorAsync` con 3 bingos del organizador y `pageSize = 2` → devuelve 2 bingos
  en `Bingos` y `Total = 3` — valida AC-01/AC-05 (parte de infraestructura).
- [ ] `ListarPorOrganizadorAsync` con bingos de distinta `FechaCreacionUtc` → el resultado viene
  ordenado descendente (el más reciente primero) — valida FR-03.
- [ ] `ListarPorOrganizadorAsync` con un organizador sin bingos → `Bingos` vacío, `Total = 0` —
  valida FR-05 (parte de infraestructura).
- [ ] `ListarPorOrganizadorAsync` con bingos de OTRO organizador (distinto `organizadorId`) →
  ninguno de esos bingos aparece en el resultado — valida FR-04/NFR-02 (mitigación R-01 del threat
  model, a nivel de infraestructura).
- [ ] `ListarPorOrganizadorAsync` con `page = 2`, `pageSize = 2` y 3 bingos totales del organizador
  → devuelve el bingo restante (1 ítem) — valida FR-02 (paginación, segunda página).

**Completion criterion**
Los 5 tests pasan contra SQL Server real (integración); el repositorio nunca devuelve bingos de un
`organizadorId` distinto al solicitado.

## Block 2 — Application + Api: orquestación y endpoint

**Files**
- `backend/BingoCart.Application/Bingos/Dtos/ListarBingosQuery.cs` (new) — `sealed record
  ListarBingosQuery([Range(1, int.MaxValue)] int Page = 1, [Range(1, int.MaxValue)] int PageSize =
  20)` — record posicional, DataAnnotations en el constructor primario (decisión de PLAN, ver
  arriba).
- `backend/BingoCart.Application/Bingos/Dtos/BingoListadoResponse.cs` (new) — `sealed record
  BingoListadoResponse(IReadOnlyList<BingoCreadoResponse> Items, int Total, int TotalPaginas, int
  Page, int PageSize)` — reutiliza `BingoCreadoResponse` (FEAT-003) por ítem (decisión de PLAN, ver
  arriba).
- `backend/BingoCart.Application/Bingos/IBingoService.cs` (modified) — agrega al puerto:
  `Task<BingoListadoResponse> ListarPropiosAsync(Guid organizadorId, int page, int pageSize)`.
- `backend/BingoCart.Application/Bingos/BingoService.cs` (modified) — implementa
  `ListarPropiosAsync`:
  1. `var pageSizeClamped = Math.Min(pageSize, 100);` — mitigación R-02 del threat model, defensa en
     profundidad más allá del `[Range]` del DTO (que valida el mínimo, no el máximo).
  2. `var paginados = await _bingoRepository.ListarPorOrganizadorAsync(organizadorId, page,
     pageSizeClamped);`
  3. Mapea cada `Bingo` de `paginados.Bingos` a `BingoCreadoResponse(b.Id, b.NombreEvento,
     b.FechaSorteoUtc, b.CantidadCartones, b.CostoPorCarton)`.
  4. `var totalPaginas = paginados.Total == 0 ? 0 : (int)Math.Ceiling(paginados.Total /
     (double)pageSizeClamped);`
  5. Devuelve `new BingoListadoResponse(items, paginados.Total, totalPaginas, page, pageSizeClamped)`
     — el `pageSize` devuelto es el CLAMPEADO, no el solicitado, para que la respuesta sea honesta
     sobre qué tamaño de página se aplicó realmente.
- `backend/BingoCart.Api/Controllers/BingosController.cs` (modified) — agrega: `[HttpGet]
  public async Task<ActionResult<BingoListadoResponse>> Listar([FromQuery] ListarBingosQuery
  query)` con `[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]` (mismo
  atributo a nivel de clase que ya cubre `Crear`, no hace falta repetirlo por método). Lee
  `Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!)` (mismo patrón que `Crear`, mitigación
  R-01 del threat model), llama a `IBingoService.ListarPropiosAsync(organizadorId, query.Page,
  query.PageSize)`, devuelve `Ok(response)` (200). **Sin** `[EnableRateLimiting]` (decisión de PLAN,
  ver arriba).

**API contract**
- Method + path: `GET /api/bingos?page={int}&pageSize={int}` (ambos opcionales, default `page=1`,
  `pageSize=20`)
- Response 200: `{ "items": [{ "id": "guid", "nombreEvento": "string", "fechaSorteoUtc": "string",
  "cantidadCartones": "int", "costoPorCarton": "decimal" }], "total": "int", "totalPaginas": "int",
  "page": "int", "pageSize": "int" }`
- Response 400: `page` o `pageSize` inválidos (≤0 o no numérico) — `{ "error": "DatosInvalidos",
  "message": "..." }`, vía `InvalidModelStateResponseFactory` ya existente (mismo formato que
  FEAT-003).
- Response 401: sin autenticación (cookie `bingocart_auth` ausente/inválida/expirada) — pipeline ya
  existente, sin código adicional.
- Auth: JWT Bearer vía cookie httpOnly (`[Authorize]`), mismo mecanismo que `Crear`/`GET
  /api/organizadores/perfil`.

**Input validation**
`[Range(1, int.MaxValue)]` en `Page`/`PageSize` de `ListarBingosQuery` (ver Files) — rechaza ≤0 y
no-numérico con 400 automático. `PageSize` > 100 NO se rechaza — se clampea en `BingoService` (ver
Logic de Files), consistente con AC-06 del PRD ("limitarlo a 100, no rechazar la request").

**Error handling**
Ningún catch nuevo en `ExceptionHandlingMiddleware` — este bloque no introduce excepciones de
dominio; el 400 de `page`/`pageSize` inválidos lo maneja el pipeline de `[ApiController]` ya
existente, y el 401 lo maneja `[Authorize]`.

**Required tests**
- [ ] `BingoServiceTests` (unit, mock de `IBingoRepository`): `ListarPropiosAsync` con datos válidos
  → `BingoListadoResponse` correcto (items mapeados desde `Bingo` a `BingoCreadoResponse`, `Total`,
  `TotalPaginas` calculados bien) — valida AC-01 (orquestación).
- [ ] `BingoServiceTests`: `ListarPropiosAsync` con `pageSize = 500` → el repositorio se invoca con
  `pageSize = 100` (verificación explícita del argumento recibido por el mock) — valida AC-06.
- [ ] `BingoServiceTests`: `ListarPropiosAsync` con un organizador sin bingos → `Items` vacío,
  `Total = 0`, `TotalPaginas = 0` — valida AC-02 (parte de orquestación).
- [ ] `BingosControllerTests` (integración, `WebApplicationFactory` + SQL Server real, mismo patrón
  que el resto de FEAT-003): `GET /api/bingos` sin autenticación → 401 — valida AC-03.
- [ ] `BingosControllerTests`: login real + creación previa de 2 bingos vía `POST /api/bingos`, luego
  `GET /api/bingos` → 200 con los 2 bingos, ordenados por fecha de creación descendente (el creado
  último aparece primero) — valida AC-01 end-to-end.
- [ ] `BingosControllerTests`: organizador con 7 bingos creados, `GET
  /api/bingos?page=2&pageSize=5` → 200 con los 2 bingos restantes (posiciones 6 y 7), `total = 7`,
  `totalPaginas = 2` — valida AC-05 end-to-end.
- [ ] `BingosControllerTests`: `GET /api/bingos?page=0` → 400 `DatosInvalidos` — valida AC-07
  (representativo del caso ≤0).
- [ ] `BingosControllerTests`: `GET /api/bingos?pageSize=abc` (no numérico) → 400 `DatosInvalidos`
  — valida AC-07 (representativo del caso no-numérico, confirma también el binding por constructor
  del record desde query string).
- [ ] `BingosControllerTests`: organizador A crea 1 bingo, organizador B (autenticado, distinto de
  A) hace `GET /api/bingos` → 200 con `items` vacío, `total = 0` — **nunca** un bingo de A — valida
  AC-04 end-to-end (mitigación R-01 del threat model).
- [ ] `BingosControllerTests`: organizador autenticado sin ningún bingo creado → `GET /api/bingos` →
  200 con `items` vacío y `total = 0` (nunca 404) — valida AC-02 end-to-end.

**Completion criterion**
Los 10 tests pasan; `GET /api/bingos` nunca devuelve un bingo de un `organizadorId` distinto al
autenticado (verificado explícitamente por AC-04); `pageSize` nunca excede 100 en la respuesta aunque
se solicite más.

## Final verification

`dotnet build`/`dotnet test` sobre `backend/BingoCart.sln` en verde, incluyendo los 15 tests
automatizados nuevos de los Blocks 1-2 (5+3+7, aproximado — el número exacto puede variar
levemente en CODE sin que eso invalide la cobertura). Un organizador autenticado que consulta `GET
/api/bingos` recibe exactamente sus propios bingos, paginados y ordenados por fecha de creación
descendente, sin fuga hacia otros organizadores en ningún escenario probado. Ningún frontend se toca
en este ticket (confirmado backend-only por el PRD y el impact scan, mismo criterio que FEAT-003).
