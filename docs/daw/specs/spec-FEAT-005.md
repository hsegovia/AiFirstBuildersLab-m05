# Spec FEAT-005: Directorio público de organizadores con evento activo

| Field | Value |
|-------|-------|
| Ticket | FEAT-005 |
| PRD | docs/daw/prd/prd-FEAT-005.md |
| Tier | FEATURE |
| Date | 2026-08-19 |
| Spec loops | 0 |

## Summary

Implementa `GET /api/organizadores/directorio` (público, sin autenticación): cualquier visitante
lista los organizadores que tienen un bingo con `FechaSorteoUtc` futura, paginado y ordenado por
fecha de sorteo ascendente, mostrando únicamente nombre de la organización + nombre del evento +
fecha de sorteo — nunca CUIT/mail/teléfono. Primer endpoint del proyecto que cruza el agregado
`Bingo` con la tabla de Identity (`ApplicationUser`), vía un repositorio dedicado nuevo.
**Backend-only**, sin pantalla de directorio en el frontend todavía.

## Coverage: PRD → blocks

| Requirement | Covered by |
|---|---|
| FR-01 | Block 2 |
| FR-02 | Block 1, Block 2 |
| FR-03 | Block 1, Block 2 |
| FR-04 | Block 1 |
| FR-05 | Block 1, Block 2 |
| FR-06 | Block 1, Block 2 |
| NFR-01 | Strategy: índice nuevo `HasIndex(FechaSorteoUtc)` en `Bingos` (Block 1) para que el filtro+orden del JOIN no requiera table scan; `Skip`/`Take` a nivel SQL Server, no en memoria. |
| NFR-02 | Strategy: `DirectorioRepository.ListarActivosAsync` proyecta con un `Select()` LINQ estrictamente tipado a `DirectorioOrganizadorItem` (3 campos) — nunca materializa `ApplicationUser` completo; test dedicado (AC-08) confirma ausencia de CUIT/mail/teléfono en la respuesta real. |

## Dependencies between blocks

Block 1 (índice + repositorio del directorio) no depende de nada nuevo — reutiliza `Bingo` (Domain,
FEAT-003) y `ApplicationUser` (Infrastructure/Identity, FEAT-001a) tal cual existen. Block 2
(Application + Api) depende de Block 1 (consume `IDirectorioRepository`). Orden: 1 → 2.

**Decisiones cerradas en PLAN (no reabrir en CODE):**

- **`IDirectorioRepository` nuevo, no agregado a `IBingoRepository`**: esta consulta cruza `Bingos`
  con `Users` (Identity) — no es un concern del agregado `Bingo` (que `IBingoRepository` encapsula),
  sino una consulta de reporte propia del directorio de organizadores. Vive en
  `Application/Organizadores/`, coherente con la organización por feature ya vigente.
- **`DirectorioRepository` accede a `AppDbContext.Users` directamente, bypaseando
  `IIdentityGateway`**: `IIdentityGateway` está documentado como el puerto hacia la persistencia de
  *credenciales* (`ExisteMailAsync`, `CrearUsuarioAsync`, `AutenticarAsync`) — un contrato de
  identidad, no un repositorio general de lectura sobre `ApplicationUser`. Esta consulta es de solo
  lectura/proyección para un reporte público, categóricamente distinta de una operación de
  identidad. Mismo criterio arquitectónico que ya usa `BingoRepository` (accede a `AppDbContext`
  directo, sin un puerto intermedio adicional). El `Select()` LINQ estricto a `DirectorioOrganizadorItem`
  (nunca `ApplicationUser` completo) es la garantía de que este bypass no reintroduce el problema que
  `IIdentityGateway` existe para evitar (exponer detalles de Identity fuera de su capa).
- **Records dedicados, no tuplas**: `DirectorioPaginado`, `DirectorioOrganizadorItem`,
  `DirectorioResponse` — mismo patrón ya establecido (`BingosPaginados`, `ResultadoAutenticacion`,
  `TokenGenerado`).
- **`ListarDirectorioQuery` con `[Range]`**: mismo criterio que `ListarBingosQuery` de FEAT-004 —
  sin invariante de dominio que proteger (la paginación no es una regla de negocio), el 400
  automático de `InvalidModelStateResponseFactory` es el comportamiento correcto (AC-06).
- **Sin caché**: el directorio se consulta en vivo contra la base en cada request — no hay
  requerimiento de caché en el PRD, y agregar una capa de invalidación sería complejidad no pedida.
- **Rate limiting nuevo (`"directorio"`), particionado por IP**: mitigación R-02 del threat model
  (`docs/daw/security/threat-FEAT-005.md`) — mismo mecanismo que la política `"registro"` ya
  existente (`FixedWindowLimiter` por `RemoteIpAddress`, único criterio válido para un endpoint
  `[AllowAnonymous]`), con un límite más generoso (30 requests/5 min vs. 5/1 min de `"registro"`)
  porque navegar el directorio paginado es un uso legítimo esperado con más tráfico que un
  formulario de alta.
- **`[AllowAnonymous]` explícito**: mismo patrón ya usado por `registro`/`login` en
  `OrganizadoresController`, aunque el controller no tenga `[Authorize]` a nivel de clase — mantiene
  la intención visible en el propio endpoint.

## Block 1 — Infraestructura: índice + repositorio del directorio

**Files**
- `backend/BingoCart.Infrastructure/Data/AppDbContext.cs` (modified) — en la configuración de
  `Bingo` (`OnModelCreating`), agrega `entity.HasIndex(b => b.FechaSorteoUtc)` (no único —
  múltiples bingos pueden compartir fecha de sorteo).
- `backend/BingoCart.Infrastructure/Data/Migrations/<timestamp>_AddIndiceFechaSorteoBingos.cs`
  (new, generada con `dotnet ef migrations add AddIndiceFechaSorteoBingos` — mismo mecanismo que las
  migraciones existentes).
- `backend/BingoCart.Application/Organizadores/Dtos/DirectorioOrganizadorItem.cs` (new) — `sealed
  record DirectorioOrganizadorItem(string NombreOrganizacion, string NombreEvento, DateTime
  FechaSorteoUtc)`.
- `backend/BingoCart.Application/Organizadores/DirectorioPaginado.cs` (new) — `sealed record
  DirectorioPaginado(IReadOnlyList<DirectorioOrganizadorItem> Items, int Total)` — mismo patrón que
  `BingosPaginados` (decisión de PLAN, ver arriba).
- `backend/BingoCart.Application/Organizadores/IDirectorioRepository.cs` (new) — puerto:
  `Task<DirectorioPaginado> ListarActivosAsync(DateTime ahoraUtc, int page, int pageSize)`.
- `backend/BingoCart.Infrastructure/Organizadores/DirectorioRepository.cs` (new) — implementa el
  puerto contra `AppDbContext`: `_context.Bingos.Join(_context.Users, b => b.OrganizadorId, u =>
  u.Id, (b, u) => new { b, u }).Where(x => x.b.FechaSorteoUtc > ahoraUtc)`, `CountAsync()` para el
  total, `.OrderBy(x => x.b.FechaSorteoUtc).Skip((page - 1) * pageSize).Take(pageSize)
  .Select(x => new DirectorioOrganizadorItem(x.u.NombreOrganizacion, x.b.NombreEvento,
  x.b.FechaSorteoUtc)).ToListAsync()` — proyección LINQ estricta (mitigación R-01 del threat model:
  nunca `Select(x => x.u)` ni equivalente).

**Logic**
Capa de infraestructura pura — sin decisiones de negocio (qué es "activo", el máximo de `pageSize`):
eso lo decide Application (Block 2). Único punto del proyecto que cruza `Bingos` con la tabla de
Identity; el cruce se hace por `OrganizadorId == Id` (FK lógica, sin `HasForeignKey` de EF Core —
mismo criterio ya usado para `Bingo.OrganizadorId`, confirmado en FEAT-003).

**API contract**
N/A — este bloque no expone ningún endpoint.

**Data model**
Índice nuevo sobre `Bingos.FechaSorteoUtc` (no único). Sin nuevas tablas ni columnas.

**Input validation**
`ahoraUtc`/`page`/`pageSize` ya llegan validados por el momento en que Application invoca este
método (Block 2) — este bloque no revalida, confía en el contrato del llamador.

**Error handling**
N/A — sin excepciones de negocio nuevas; una consulta EF Core fallida se propaga sin capturar.

**Required tests**
- [ ] `ListarActivosAsync` con 2 organizadores (cada uno con un `ApplicationUser` real + un
  `Bingo` con `FechaSorteoUtc` futura) → devuelve los 2, con `NombreOrganizacion`/`NombreEvento`/
  `FechaSorteoUtc` correctos — valida AC-01/AC-02 (parte de infraestructura). **Nota de test:** a
  diferencia de `BingoRepositoryTests` (que usa `Guid.NewGuid()` sueltos como `OrganizadorId`, sin
  fila real de `ApplicationUser`, porque no hay FK física), estos tests SÍ necesitan crear filas
  `ApplicationUser` reales (mismo `Id` que `Bingo.OrganizadorId`) porque el repositorio hace un JOIN
  real — sin el `ApplicationUser`, la fila no aparece en el resultado.
- [ ] `ListarActivosAsync` con un organizador cuyo único bingo tiene `FechaSorteoUtc` pasada →
  no aparece en el resultado — valida AC-02.
- [ ] `ListarActivosAsync` sin ningún organizador con bingo activo → `Items` vacío, `Total = 0` —
  valida AC-03 (parte de infraestructura).
- [ ] `ListarActivosAsync` con bingos de distinta `FechaSorteoUtc` → el resultado viene ordenado
  ascendente (el sorteo más próximo primero) — valida FR-04.
- [ ] `ListarActivosAsync` con `page = 2`, `pageSize = 2` y 3 organizadores activos → devuelve el
  restante (1 ítem) — valida FR-03 (paginación, segunda página).
- [ ] `ListarActivosAsync` — inspección explícita del resultado proyectado: ningún campo del
  `ApplicationUser` más allá de `NombreOrganizacion` llega al tipo de retorno (verificado
  estructuralmente: `DirectorioOrganizadorItem` no tiene propiedades para CUIT/mail/teléfono, así
  que es estructuralmente imposible que el repositorio los devuelva) — valida NFR-02/AC-08 a nivel
  de infraestructura.

**Completion criterion**
Los 6 tests pasan contra SQL Server real (integración); el repositorio nunca incluye un
organizador sin bingo con `FechaSorteoUtc` futura, y la proyección nunca expone más que los 3 campos
del DTO.

## Block 2 — Application + Api: orquestación y endpoint

**Files**
- `backend/BingoCart.Application/Organizadores/Dtos/ListarDirectorioQuery.cs` (new) — `sealed
  record ListarDirectorioQuery([Range(1, int.MaxValue)] int Page = 1, [Range(1, int.MaxValue)] int
  PageSize = 20)` — record posicional, mismo estilo que `ListarBingosQuery` (decisión de PLAN).
- `backend/BingoCart.Application/Organizadores/Dtos/DirectorioResponse.cs` (new) — `sealed record
  DirectorioResponse(IReadOnlyList<DirectorioOrganizadorItem> Items, int Total, int TotalPaginas,
  int Page, int PageSize)`.
- `backend/BingoCart.Application/Organizadores/IOrganizadorService.cs` (modified) — agrega al
  puerto: `Task<DirectorioResponse> ListarDirectorioAsync(int page, int pageSize)`.
- `backend/BingoCart.Application/Organizadores/OrganizadorService.cs` (modified) — constructor
  agrega `IDirectorioRepository` y `TimeProvider` (mismo patrón que `BingoService`, que ya inyecta
  `TimeProvider`). Implementa `ListarDirectorioAsync`:
  1. `var pageSizeClamped = Math.Min(pageSize, 100);` — defensa en profundidad (mitigación R-02 vía
     control de carga, más allá del `[Range]` del DTO).
  2. `var ahoraUtc = _timeProvider.GetUtcNow().UtcDateTime;`
  3. `var paginado = await _directorioRepository.ListarActivosAsync(ahoraUtc, page,
     pageSizeClamped);`
  4. `var totalPaginas = paginado.Total == 0 ? 0 : (int)Math.Ceiling(paginado.Total /
     (double)pageSizeClamped);`
  5. Devuelve `new DirectorioResponse(paginado.Items, paginado.Total, totalPaginas, page,
     pageSizeClamped)`.
- `backend/BingoCart.Api/Controllers/OrganizadoresController.cs` (modified) — agrega:
  `[HttpGet("directorio")] [AllowAnonymous] [EnableRateLimiting("directorio")]
  [ProducesResponseType(typeof(DirectorioResponse), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)] public async
  Task<ActionResult<DirectorioResponse>> Directorio([FromQuery] ListarDirectorioQuery query)` —
  delega 100% a `IOrganizadorService.ListarDirectorioAsync(query.Page, query.PageSize)`, devuelve
  `Ok(response)` (200). Mismo patrón `[AllowAnonymous]` que `RegistrarAsync`/`Login`.
- `backend/BingoCart.Api/Program.cs` (modified) — registra
  `builder.Services.AddScoped<IDirectorioRepository, DirectorioRepository>();` (Scoped, mismo
  lifetime que `IBingoRepository` — depende de `AppDbContext`). En `AddRateLimiter`, agrega la
  política `"directorio"`: `RateLimitPartition.GetFixedWindowLimiter(partitionKey:
  httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown", factory: _ => new
  FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(5) })` —
  particionada por IP (mismo criterio que `"registro"`, endpoint `[AllowAnonymous]`), mitigación
  R-02 de threat modeling.

**API contract**
- Method + path: `GET /api/organizadores/directorio?page={int}&pageSize={int}` (ambos opcionales,
  default `page=1`, `pageSize=20`)
- Response 200: `{ "items": [{ "nombreOrganizacion": "string", "nombreEvento": "string",
  "fechaSorteoUtc": "string" }], "total": "int", "totalPaginas": "int", "page": "int", "pageSize":
  "int" }`
- Response 400: `page` o `pageSize` inválidos (≤0 o no numérico) — `{ "error": "DatosInvalidos",
  "message": "..." }`, vía `InvalidModelStateResponseFactory` ya existente.
- Response 429: demasiadas requests en la ventana de tiempo (más de 30 en 5 minutos para la misma
  IP) — manejado automáticamente por `AddRateLimiter`/`[EnableRateLimiting("directorio")]`, sin
  código adicional (mismo mecanismo ya usado por `"registro"`), mitigación R-02.
- Auth: ninguna — endpoint público (`[AllowAnonymous]`).

**Input validation**
`[Range(1, int.MaxValue)]` en `Page`/`PageSize` de `ListarDirectorioQuery` — rechaza ≤0 y
no-numérico con 400 automático. `PageSize` > 100 NO se rechaza — se clampea en `OrganizadorService`.

**Error handling**
Ningún catch nuevo en `ExceptionHandlingMiddleware` — este bloque no introduce excepciones de
dominio; el 400 de `page`/`pageSize` inválidos lo maneja el pipeline de `[ApiController]` ya
existente, el 429 lo maneja el middleware de rate limiting ya existente.

**Required tests**
- [ ] `OrganizadorServiceTests` (unit, mock de `IDirectorioRepository`): `ListarDirectorioAsync`
  con datos válidos → `DirectorioResponse` correcto (`Items`, `Total`, `TotalPaginas` calculados
  bien) — valida AC-01 (orquestación). **Nota:** actualizar el helper `CrearService` del archivo
  (pasa de 2 a 4 argumentos del constructor de `OrganizadorService`) — sin esto, el archivo no
  compila.
- [ ] `OrganizadorServiceTests`: `ListarDirectorioAsync` con `pageSize = 500` → el repositorio se
  invoca con `pageSize = 100` (verificación explícita del argumento recibido por el mock) — valida
  AC-05.
- [ ] `OrganizadoresControllerTests` (integración, `WebApplicationFactory` + SQL Server real): `GET
  /api/organizadores/directorio` SIN cookie de autenticación → 200 (no 401) — valida AC-07 (endpoint
  público).
- [ ] `OrganizadoresControllerTests`: registro real de 2 organizadores + creación de 1 bingo cada
  uno (`POST /api/bingos`, requiere login) con `FechaSorteoUtc` futura, luego `GET
  /api/organizadores/directorio` sin autenticación → 200 con los 2, ordenados por `FechaSorteoUtc`
  ascendente — valida AC-01 end-to-end.
- [ ] `OrganizadoresControllerTests`: organizador con 7 bingos activos sembrados (mismo criterio de
  seed directo que FEAT-004, dado que un organizador solo puede tener 1 bingo activo por vez —
  requiere 7 organizadores distintos, no 7 bingos del mismo), `GET
  /api/organizadores/directorio?page=2&pageSize=5` → 200 con los 2 restantes, `total = 7`,
  `totalPaginas = 2` — valida AC-04 end-to-end.
- [ ] `OrganizadoresControllerTests`: `GET /api/organizadores/directorio?page=0` → 400
  `DatosInvalidos` — valida AC-06.
- [ ] `OrganizadoresControllerTests`: sin ningún organizador con bingo activo (base limpia) → `GET
  /api/organizadores/directorio` → 200 con `items` vacío y `total = 0` — valida AC-03 end-to-end.
- [ ] `OrganizadoresControllerTests`: organizador con CUIT/mail/teléfono registrados y bingo activo
  → `GET /api/organizadores/directorio` → la respuesta JSON NO contiene los strings del CUIT, del
  mail ni del teléfono de ese organizador (aserción explícita sobre el body crudo, no solo sobre el
  tipo deserializado) — valida AC-08/NFR-02 end-to-end, mitigación R-01 del threat model.
- [ ] `OrganizadoresControllerTests`: 31 requests consecutivas desde el mismo cliente de test a
  `GET /api/organizadores/directorio` dentro de la ventana de 5 minutos → el request 31 devuelve
  429 — valida la mitigación R-02 de threat modeling.

**Completion criterion**
Los 10 tests pasan; `GET /api/organizadores/directorio` nunca expone CUIT/mail/teléfono
(verificado explícitamente por AC-08); un organizador sin bingo activo nunca aparece; más de 30
requests en 5 minutos desde la misma IP son rechazadas con 429.

## Final verification

`dotnet build`/`dotnet test` sobre `backend/BingoCart.sln` en verde, incluyendo los 16 tests
automatizados nuevos de los Blocks 1-2 (6+2+8, aproximado). Un visitante sin autenticar que consulta
`GET /api/organizadores/directorio` recibe exactamente los organizadores con bingo de sorteo futuro,
paginados y ordenados por fecha de sorteo ascendente, sin exponer CUIT/mail/teléfono en ningún
escenario probado, y sin poder exceder 30 requests/5 min desde la misma IP. Ningún frontend se toca
en este ticket (confirmado backend-only por el PRD y el impact scan, mismo criterio que
FEAT-003/FEAT-004).
