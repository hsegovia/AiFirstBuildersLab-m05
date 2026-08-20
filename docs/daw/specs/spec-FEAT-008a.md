# Spec FEAT-008a: Descubrimiento de cartones

| Field | Value |
|-------|-------|
| Ticket | FEAT-008a |
| PRD | docs/daw/prd/prd-FEAT-008a.md |
| Tier | FEATURE |
| Date | 2026-08-20 |
| Spec loops | 1 |

## Summary

Implementa `GET /api/cartones/descubrimiento` (Método 1 — 5 cartones aleatorios de cualquier bingo
activo) y `GET /api/cartones/organizador/{organizadorId}` (Método 2 — 5 cartones aleatorios del
bingo activo de un organizador dado), ambos públicos y sin autenticación. Primer punto del proyecto
que hace selección aleatoria a nivel de base de datos (`ORDER BY NEWID()`, SQL Server) — sin
precedente en el codebase, confirmado por impact scan. **Backend-only**, sin pantalla de
descubrimiento en el frontend todavía.

## Coverage: PRD → blocks

| Requirement | Covered by |
|---|---|
| FR-01 | Block 1, Block 2 |
| FR-02 | Block 1, Block 2 |
| FR-03 | Block 1, Block 2 |
| FR-04 | Block 1 |
| FR-05 | Block 1, Block 2 |
| FR-06 | Block 1, Block 2 |
| FR-07 | Block 1 |
| NFR-01 | Strategy: selección aleatoria resuelta en SQL Server vía `FromSqlInterpolated` con `ORDER BY NEWID()` (Block 1) — nunca se cargan en memoria los cartones candidatos completos de un bingo para elegir 5. |
| NFR-02 | Strategy: política de rate limiting nueva `"descubrimiento"` (Block 2), 60 req/5min por IP, mismo mecanismo `FixedWindowLimiter` ya usado por `"directorio"` (FEAT-005) y `"registro"` (FEAT-001a). |

## Dependencies between blocks

Block 1 (Infraestructura: `IDescubrimientoRepository`) no depende de nada nuevo — reutiliza
`Bingo`/`Carton` (Domain, FEAT-003) y `AppDbContext.Users` (mismo patrón de bypass de
`IIdentityGateway` que `DirectorioRepository`, FEAT-005) ya existentes. Block 2 (Application + Api)
depende de Block 1. Orden: 1 → 2.

**Decisiones cerradas en PLAN (no reabrir en CODE):**

- **Selección aleatoria vía `FromSqlInterpolated` + `ORDER BY NEWID()`**, no `.OrderBy(_ =>
  Guid.NewGuid())` (no traducible a SQL por EF Core — lanzaría en tiempo de ejecución, client-side
  evaluation deshabilitada por default) ni `TABLESAMPLE` (sesga hacia bloques de página, no
  garantiza uniformidad con conjuntos chicos como 5 de N). `NEWID()` es específico de SQL Server,
  coherente con el stack declarado (`AGENTS.md`).
- **Dos consultas separadas, no una sola con JOIN a `Users` en la selección aleatoria**: la query
  aleatoria (`SELECT TOP (N) c.* FROM Cartones c INNER JOIN Bingos b ON b.Id = c.BingoId WHERE
  b.FechaSorteoUtc > {ahoraUtc} ORDER BY NEWID()`) solo necesita `Bingos`+`Cartones` para filtrar
  "activo" — el nombre de organización se resuelve después, en una segunda consulta LINQ normal
  sobre los (máximo 5) `BingoId` distintos obtenidos. Evita la complejidad de proyectar un tipo
  keyless desde SQL crudo con JOIN a `Users`, y el costo extra es insignificante (≤5 filas).
- **`IDescubrimientoRepository` nuevo en `Application/Descubrimiento/`**, no agregado a
  `IBingoRepository` ni a `IDirectorioRepository`: cruza `Bingos`+`Cartones`+`Users` con una lógica
  de selección propia (aleatoriedad), categóricamente distinta de ambos repositorios existentes —
  mismo criterio de PLAN ya aplicado en FEAT-005 para justificar un puerto separado del agregado
  `Bingo`.
- **`OrganizadorNoEncontradoException` nueva, en `Domain/Organizadores/Exceptions/`** (no en
  `Domain/Bingos/Exceptions/`, porque lo que no se encuentra es un organizador, no un bingo) —
  mismo patrón que `BingoNoEncontradoException` (FEAT-007): hereda `DomainException`, un `catch`
  más en el middleware, 404.
- **`ExisteOrganizadorAsync` es una consulta separada de `ObtenerBingoActivoDeOrganizadorAsync`**,
  no una sola que infiera "no existe" de "no tiene bingo activo": son dos respuestas HTTP distintas
  (404 vs. 200 vacío, AC-03/AC-04) y colapsarlas en una sola consulta booleana perdería esa
  distinción.
- **Nuevo controller `CartonesController`** (no se agrega a `BingosController`, que tiene
  `[Authorize]` a nivel de clase, ni a `OrganizadoresController`): ambos endpoints son públicos y
  el recurso expuesto es `Carton`, no `Bingo` ni `Organizador` — mismo criterio de "el recurso
  define el controller" ya usado para separar `BingosController` de `OrganizadoresController`.
- **Respuesta como array plano** (`IReadOnlyList<CartonDescubiertoResponse>`), no un objeto
  paginado tipo `BingoListadoResponse`: el PRD no pide paginación, siempre son como máximo 5
  elementos por diseño (FR-01/FR-02), un wrapper con metadata de paginación sería complejidad no
  pedida.
- **`DirectorioOrganizadorItem` (FEAT-005, ya mergeado) gana un campo `Id`**: el directorio
  público actual (`GET /api/organizadores/directorio`) no expone el `organizadorId`, así que no hay
  forma de que un cliente que lista el directorio pueda después pedir `GET
  /api/cartones/organizador/{organizadorId}` — gap real encontrado en PLAN, no parte del alcance
  original de FEAT-005 (que no tenía todavía un consumidor de ese id). Se agrega `Guid Id` al DTO
  y a la proyección de `DirectorioRepository.ListarActivosAsync` (`x.u.Id`, ya disponible en el
  JOIN existente — no agrega ningún cruce nuevo). Es un campo agregado, no un cambio disruptivo:
  ningún consumidor existente se rompe. El `Id` de un organizador no es un dato sensible (mismo
  criterio que exponer `BingoId` en las respuestas de `Bingo`) — no reabre la mitigación R-01 del
  threat model de FEAT-005 (que protege CUIT/mail/teléfono, no el identificador).
- **Sin CSPRNG para la selección aleatoria**: RNF-07 del PRD maestro (CSPRNG obligatorio) aplica a
  la generación de los *números* de un cartón (FEAT-003), una decisión de seguridad para evitar
  cartones predecibles vendidos. Elegir *cuáles* cartones mostrar en una tanda de descubrimiento no
  es una operación de seguridad — es una función de UX/variedad, `NEWID()` es suficiente y es lo
  que el propio PRD anticipa en "Risks and Mitigations".

## Block 1 — Infraestructura: repositorio de descubrimiento + extensión del directorio

**Files**
- `backend/BingoCart.Application/Organizadores/Dtos/DirectorioOrganizadorItem.cs` (modified) —
  agrega `Guid Id` como primer campo: `sealed record DirectorioOrganizadorItem(Guid Id, string
  NombreOrganizacion, string NombreEvento, DateTime FechaSorteoUtc)`.
- `backend/BingoCart.Infrastructure/Organizadores/DirectorioRepository.cs` (modified) — la
  proyección de `ListarActivosAsync` agrega `x.u.Id`: `new DirectorioOrganizadorItem(x.u.Id,
  x.u.NombreOrganizacion, x.b.NombreEvento, x.b.FechaSorteoUtc)`.
- `backend/BingoCart.Domain/Organizadores/Exceptions/OrganizadorNoEncontradoException.cs` (new) —
  hereda `DomainException`, mismo patrón que `BingoNoEncontradoException`.
- `backend/BingoCart.Application/Descubrimiento/IDescubrimientoRepository.cs` (new) — puerto:
  ```csharp
  Task<IReadOnlyList<Carton>> ObtenerAleatoriosGlobalAsync(DateTime ahoraUtc, int cantidad);
  Task<bool> ExisteOrganizadorAsync(Guid organizadorId);
  Task<Guid?> ObtenerBingoActivoDeOrganizadorAsync(Guid organizadorId, DateTime ahoraUtc);
  Task<IReadOnlyList<Carton>> ObtenerAleatoriosDeBingoAsync(Guid bingoId, int cantidad);
  Task<IReadOnlyList<BingoResumen>> ObtenerResumenBingosAsync(IReadOnlyCollection<Guid> bingoIds);
  ```
- `backend/BingoCart.Application/Descubrimiento/Dtos/BingoResumen.cs` (new) — `sealed record
  BingoResumen(Guid Id, string NombreOrganizacion, string NombreEvento, decimal CostoPorCarton,
  DateTime FechaSorteoUtc)`.
- `backend/BingoCart.Infrastructure/Descubrimiento/DescubrimientoRepository.cs` (new) — implementa
  el puerto:
  - `ObtenerAleatoriosGlobalAsync`:
    ```csharp
    await _context.Cartones
        .FromSqlInterpolated($@"
            SELECT TOP ({cantidad}) c.*
            FROM Cartones c
            INNER JOIN Bingos b ON b.Id = c.BingoId
            WHERE b.FechaSorteoUtc > {ahoraUtc}
            ORDER BY NEWID()")
        .AsNoTracking()
        .ToListAsync();
    ```
  - `ExisteOrganizadorAsync` → `_context.Users.AnyAsync(u => u.Id == organizadorId)`.
  - `ObtenerBingoActivoDeOrganizadorAsync` → `_context.Bingos.Where(b => b.OrganizadorId ==
    organizadorId && b.FechaSorteoUtc > ahoraUtc).Select(b => (Guid?)b.Id).FirstOrDefaultAsync()`.
  - `ObtenerAleatoriosDeBingoAsync` → mismo patrón `FromSqlInterpolated` que el global, pero
    `WHERE c.BingoId = {bingoId} ORDER BY NEWID()`.
  - `ObtenerResumenBingosAsync` → LINQ normal: `_context.Bingos.Join(_context.Users, ...).Where(b
    => bingoIds.Contains(b.Id)).Select(...)` — mismo patrón de JOIN a `Users` ya usado en
    `DirectorioRepository` (FEAT-005), reutilizado acá, no copiado a ciegas: esta consulta no
    filtra por "activo" (los bingoIds ya vienen filtrados) ni pagina.

**Logic**
Capa de infraestructura pura — sin decisiones de negocio (qué es "activo" ya viene resuelto por el
filtro SQL explícito, no hay ambigüedad que decidir acá; cuántos cartones pedir lo decide
Application en Block 2).

**API contract**
N/A — este bloque no expone ningún endpoint.

**Data model**
Sin cambios de esquema — reutiliza las tablas `Bingos`/`Cartones`/`Users` (Identity) tal cual
existen.

**Input validation**
`cantidad`/`bingoId`/`organizadorId` llegan validados por el contrato del llamador (Application,
Block 2) — este bloque no revalida.

**Error handling**
Sin excepciones nuevas propias de este bloque — una consulta EF Core fallida se propaga sin
capturar, `OrganizadorNoEncontradoException` la lanza Block 2 (Application), no el repositorio.

**Required tests**
- [ ] `DirectorioRepositoryTests` (test existente de FEAT-005, actualizado): `ListarActivosAsync`
  con un organizador real → el `Id` del `DirectorioOrganizadorItem` devuelto coincide exactamente
  con el `Guid` del `ApplicationUser` sembrado en el test — valida la extensión del directorio que
  este bloque agrega (prerequisito real de AC-02, no un AC propio de FEAT-008a).
- [ ] `ObtenerAleatoriosGlobalAsync` con 2 bingos activos de organizadores distintos y uno vencido
  (sorteo pasado) → nunca devuelve cartones del bingo vencido — valida AC-07 (parte de
  infraestructura).
- [ ] `ObtenerAleatoriosGlobalAsync` con menos de 5 cartones elegibles en total → devuelve todos
  los disponibles, sin error — valida AC-05 (parte de infraestructura).
- [ ] `ObtenerAleatoriosGlobalAsync` sin ningún bingo activo → lista vacía — valida AC-09.
- [ ] `ObtenerAleatoriosGlobalAsync` con suficientes cartones elegibles → los 5 devueltos son
  distintos entre sí (`Select(c => c.Id).Distinct().Count() == 5`) — valida AC-08.
- [ ] `ExisteOrganizadorAsync` con un organizador real registrado → `true`; con un `Guid` aleatorio
  → `false` — valida la parte de infraestructura de AC-03/AC-04.
- [ ] `ObtenerBingoActivoDeOrganizadorAsync` con un organizador con bingo activo → devuelve el
  `Id` correcto; con un organizador cuyo único bingo tiene sorteo pasado → `null` — valida AC-04/
  AC-07 (parte de infraestructura).
- [ ] `ObtenerAleatoriosDeBingoAsync` con un bingo con más de 5 cartones → devuelve exactamente 5,
  todos con el `BingoId` correcto — valida AC-02 (parte de infraestructura).
- [ ] `ObtenerAleatoriosDeBingoAsync` con un bingo con menos de 5 cartones → devuelve todos, sin
  error — valida AC-05 (parte de infraestructura, Método 2).
- [ ] `ObtenerResumenBingosAsync` con 2 `bingoId` reales → devuelve `NombreOrganizacion`/
  `NombreEvento`/`CostoPorCarton`/`FechaSorteoUtc` correctos para ambos.
- [ ] `ObtenerAleatoriosGlobalAsync` invocado 5 veces seguidas contra un pool de al menos 20
  cartones elegibles → no todas las 5 selecciones son idénticas entre sí (assert: el conjunto de
  `HashSet<Guid>` de la unión de las 5 corridas tiene más de 5 elementos distintos) — valida AC-06.
  Test estadístico, no determinístico por naturaleza: con `C(20,5) = 15504` combinaciones posibles,
  la probabilidad de que 5 corridas de `ORDER BY NEWID()` devuelvan exactamente el mismo subconjunto
  es despreciable; no se afirma que el mecanismo *nunca* pueda repetir por azar, solo que no está
  cacheado ni es determinístico (que es literalmente lo que pide AC-06).

**Completion criterion**
Los 11 tests pasan contra SQL Server real (integración); ningún método de este bloque devuelve
cartones de un bingo cuyo sorteo ya pasó; el directorio público sigue sin exponer CUIT/mail/
teléfono (el campo `Id` agregado no reabre esa mitigación, verificado por el test ya existente de
FEAT-005 que sigue en la suite sin cambios).

## Block 2 — Application + Api: orquestación, endpoints y mapeo de errores

**Files**
- `backend/BingoCart.Application/Descubrimiento/Dtos/CartonDescubiertoResponse.cs` (new) — `sealed
  record CartonDescubiertoResponse(Guid Id, string NombreOrganizacion, string NombreEvento,
  DateTime FechaSorteoUtc, decimal CostoPorCarton, IReadOnlyList<int> Numeros)`.
- `backend/BingoCart.Application/Descubrimiento/IDescubrimientoService.cs` (new) — puerto:
  ```csharp
  Task<IReadOnlyList<CartonDescubiertoResponse>> DescubrirGlobalAsync();
  Task<IReadOnlyList<CartonDescubiertoResponse>> DescubrirPorOrganizadorAsync(Guid organizadorId);
  ```
- `backend/BingoCart.Application/Descubrimiento/DescubrimientoService.cs` (new) — implementa:
  - Constante `CantidadPorTanda = 5` (privada).
  - `DescubrirGlobalAsync`: `ahoraUtc` vía `_timeProvider`; `cartones =
    _repo.ObtenerAleatoriosGlobalAsync(ahoraUtc, CantidadPorTanda)`; si vacío, devuelve lista
    vacía sin más consultas; `bingoIds = cartones.Select(c => c.BingoId).Distinct().ToList()`;
    `resumenes = _repo.ObtenerResumenBingosAsync(bingoIds)`; arma la respuesta uniendo cada
    `Carton` con su `BingoResumen` correspondiente (por `BingoId`).
  - `DescubrirPorOrganizadorAsync(organizadorId)`: si `!await
    _repo.ExisteOrganizadorAsync(organizadorId)` → `throw new
    OrganizadorNoEncontradoException("El organizador indicado no existe.")`. `bingoId = await
    _repo.ObtenerBingoActivoDeOrganizadorAsync(organizadorId, ahoraUtc)`; si `null`, devuelve lista
    vacía. Si no, `cartones = await _repo.ObtenerAleatoriosDeBingoAsync(bingoId.Value,
    CantidadPorTanda)`; `resumenes = await _repo.ObtenerResumenBingosAsync(new[] {
    bingoId.Value })` (siempre 0 o 1 elemento); arma la respuesta igual que el método global.
- `backend/BingoCart.Api/Controllers/CartonesController.cs` (new):
  ```csharp
  [AllowAnonymous]
  [ApiController]
  [Route("api/cartones")]
  public sealed class CartonesController : ControllerBase
  {
      private readonly IDescubrimientoService _descubrimientoService;

      [HttpGet("descubrimiento")]
      [EnableRateLimiting("descubrimiento")]
      [ProducesResponseType(typeof(IReadOnlyList<CartonDescubiertoResponse>), StatusCodes.Status200OK)]
      public async Task<ActionResult<IReadOnlyList<CartonDescubiertoResponse>>> Descubrimiento()
      {
          var response = await _descubrimientoService.DescubrirGlobalAsync();
          return Ok(response);
      }

      [HttpGet("organizador/{organizadorId:guid}")]
      [EnableRateLimiting("descubrimiento")]
      [ProducesResponseType(typeof(IReadOnlyList<CartonDescubiertoResponse>), StatusCodes.Status200OK)]
      [ProducesResponseType(StatusCodes.Status404NotFound)]
      public async Task<ActionResult<IReadOnlyList<CartonDescubiertoResponse>>> PorOrganizador(Guid organizadorId)
      {
          var response = await _descubrimientoService.DescubrirPorOrganizadorAsync(organizadorId);
          return Ok(response);
      }
  }
  ```
  Sin `[Authorize]` a nivel de clase (ningún endpoint requiere sesión) — `[AllowAnonymous]`
  explícito de todas formas, mismo criterio documentado ya en `OrganizadoresController` (mantiene
  la intención visible aunque no haga falta).
- `backend/BingoCart.Api/Middleware/ExceptionHandlingMiddleware.cs` (modified) — agrega:
  ```csharp
  catch (OrganizadorNoEncontradoException ex)
  {
      await ManejarExcepcionDeDominioAsync(context, ex, HttpStatusCode.NotFound, "OrganizadorNoEncontrado");
  }
  ```
- `backend/BingoCart.Api/Program.cs` (modified) — registra `AddScoped<IDescubrimientoRepository,
  DescubrimientoRepository>()`, `AddScoped<IDescubrimientoService, DescubrimientoService>()`, y en
  `AddRateLimiter`:
  ```csharp
  options.AddPolicy("descubrimiento", httpContext => RateLimitPartition.GetFixedWindowLimiter(
      partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
      factory: _ => new FixedWindowRateLimiterOptions
      {
          PermitLimit = 60,
          Window = TimeSpan.FromMinutes(5)
      }));
  ```

**API contract**
- `GET /api/cartones/descubrimiento` — sin params. Response 200: `[{ "id": "guid",
  "nombreOrganizacion": "string", "nombreEvento": "string", "fechaSorteoUtc": "string",
  "costoPorCarton": "number", "numeros": [1,2,...] }]` (0 a 5 elementos). Response 429: más de 60
  req/5min desde la misma IP. Auth: ninguna.
- `GET /api/cartones/organizador/{organizadorId}` — `organizadorId` en la ruta (Guid). Response
  200: misma forma que el global (0 a 5 elementos, siempre del mismo bingo). Response 404: `{
  "error": "OrganizadorNoEncontrado", "message": "..." }`. Response 429: igual que el método
  global. Auth: ninguna.

**Input validation**
`organizadorId:guid` en la ruta rechaza automáticamente valores no-Guid con 404 de ASP.NET Core
routing (no llega al action). Sin más input que validar (el endpoint global no recibe parámetros).

**Error handling**
`OrganizadorNoEncontradoException` → 404 (nueva en este bloque). Sin otros errores de dominio
nuevos — una falla de infraestructura no controlada cae en el `catch (Exception ex)` genérico ya
existente (500).

**Required tests**
- [ ] `DescubrimientoServiceTests` (unit, mock de `IDescubrimientoRepository`):
  `DescubrirGlobalAsync` con el repositorio devolviendo cartones de 2 bingos distintos → arma
  correctamente cada `CartonDescubiertoResponse` con los datos del `BingoResumen` que le
  corresponde por `BingoId` — valida AC-01 (orquestación).
- [ ] `DescubrimientoServiceTests`: `DescubrirGlobalAsync` con el repositorio devolviendo lista
  vacía → devuelve lista vacía sin invocar `ObtenerResumenBingosAsync` — valida AC-09
  (orquestación).
- [ ] `DescubrimientoServiceTests`: `DescubrirPorOrganizadorAsync` con `ExisteOrganizadorAsync`
  mockeado a `false` → `OrganizadorNoEncontradoException`, sin invocar
  `ObtenerBingoActivoDeOrganizadorAsync` — valida AC-03 (orquestación).
- [ ] `DescubrimientoServiceTests`: `DescubrirPorOrganizadorAsync` con organizador existente y
  `ObtenerBingoActivoDeOrganizadorAsync` mockeado a `null` → lista vacía, sin invocar
  `ObtenerAleatoriosDeBingoAsync` — valida AC-04 (orquestación).
- [ ] `DescubrimientoServiceTests`: `DescubrirPorOrganizadorAsync` con organizador con bingo activo
  → arma la respuesta correctamente con los datos de ese único bingo — valida AC-02
  (orquestación).
- [ ] `CartonesControllerTests` (integración, `WebApplicationFactory` + SQL Server real): 2
  organizadores reales registrados, cada uno con un bingo activo y cartones reales, `GET
  /api/cartones/descubrimiento` sin autenticación → 200 con hasta 5 cartones, cada uno con
  `nombreOrganizacion`/`nombreEvento` que corresponden a su propio bingo (no mezclados) — valida
  AC-01 end-to-end.
- [ ] `CartonesControllerTests`: base sin ningún bingo activo → `GET
  /api/cartones/descubrimiento` → 200 con lista vacía — valida AC-09 end-to-end.
- [ ] `CartonesControllerTests`: un organizador real con bingo activo y cartones, `GET
  /api/cartones/organizador/{id}` → 200 con hasta 5 cartones, todos del bingo de ese organizador —
  valida AC-02 end-to-end.
- [ ] `CartonesControllerTests`: `GET /api/cartones/organizador/{guid-inexistente}` → 404
  `OrganizadorNoEncontrado` — valida AC-03 end-to-end.
- [ ] `CartonesControllerTests`: organizador real sin bingo activo (o con bingo de sorteo pasado)
  → `GET /api/cartones/organizador/{id}` → 200 con lista vacía — valida AC-04/AC-07 end-to-end.
- [ ] `CartonesControllerTests`: organizador real con CUIT/mail/teléfono registrados y bingo activo
  → la respuesta cruda de `GET /api/cartones/organizador/{id}` NO contiene esos datos (mismo
  criterio de inspección de body crudo que FEAT-005, AC-08 de esa spec) — el `CUIT`/`mail`/
  `teléfono` de un organizador nunca deben filtrarse por este endpoint tampoco.
- [ ] `CartonesControllerTests`: 61 requests consecutivas a `GET /api/cartones/descubrimiento`
  desde el mismo cliente dentro de la ventana de 5 minutos → el request 61 devuelve 429 — valida
  NFR-02.

**Completion criterion**
Los 13 tests pasan; ningún cartón devuelto pertenece a un bingo con sorteo pasado; el Método 2
nunca devuelve cartones de un bingo que no sea el del organizador pedido; ningún dato personal del
organizador (CUIT/mail/teléfono) aparece en ninguna respuesta; más de 60 requests en 5 minutos
desde la misma IP son rechazadas con 429 en ambos endpoints.

## Final verification

`dotnet build`/`dotnet test` sobre `backend/BingoCart.sln` en verde, incluyendo los 24 tests
automatizados nuevos de los Blocks 1-2 (11+13). Un visitante sin autenticar puede descubrir cartones
al azar de cualquier bingo activo, o de un organizador específico con bingo activo, sin ver nunca
cartones de bingos vencidos ni datos personales de los organizadores. Ningún frontend se toca en
este ticket (confirmado backend-only por el PRD, mismo criterio que FEAT-003/FEAT-004/FEAT-005/
FEAT-007).
