# Spec FEAT-007: Editar y eliminar bingo sin compras

| Field | Value |
|-------|-------|
| Ticket | FEAT-007 |
| PRD | docs/daw/prd/prd-FEAT-007.md |
| Tier | FEATURE |
| Date | 2026-08-19 |
| Spec loops | 1 |

## Summary

Implementa `PUT /api/bingos/{id}` (editar nombre de evento, fecha de sorteo y costo por cartón) y
`DELETE /api/bingos/{id}` (eliminar bingo + cartones) para el organizador autenticado dueño del
bingo, siempre que el bingo no tenga compras registradas. Primer caso del proyecto que edita una
entidad de dominio hasta ahora inmutable (`Bingo`), y primer 404 del sistema. El borrado de cartones
al eliminar un bingo ya está resuelto a nivel de esquema (`AppDbContext.cs`, `OnDelete(Cascade)`
entre `Bingo` y `Carton`) — no requiere lógica adicional. **Backend-only**, sin pantalla de
edición/eliminación en el frontend todavía.

## Coverage: PRD → blocks

| Requirement | Covered by |
|---|---|
| FR-01 | Block 1, Block 2 |
| FR-02 | Block 1, Block 2 |
| FR-03 | Block 1, Block 2 |
| FR-04 | Block 1, Block 2 |
| FR-05 | Block 1, Block 2 |
| FR-06 | Block 1, Block 2 |
| FR-07 | Block 1 (cascade de esquema, ya existente) |
| NFR-01 | Strategy: `organizadorId` derivado exclusivamente de `ClaimTypes.NameIdentifier` en el JWT (Block 2), mismo patrón que `BingosController.Crear`/`Listar` — nunca de la ruta ni del body. |
| NFR-02 | Strategy: la eliminación es un único `DbContext.Remove(bingo)` + `SaveChangesAsync()` (Block 1) — una sola transacción implícita de EF Core; el cascade de `Cartones` ocurre en la misma sentencia `DELETE` a nivel de motor SQL, no en dos pasos separados desde la aplicación. |

## Dependencies between blocks

Block 1 (Domain + Infraestructura) no depende de nada nuevo — extiende `Bingo` (Domain, FEAT-003) e
`IBingoRepository`/`BingoRepository` (FEAT-003) ya existentes. Block 2 (Application + Api) depende de
Block 1 (consume el método `Bingo.Actualizar` y los 3 métodos nuevos del repositorio). Orden: 1 → 2.

**Decisiones cerradas en PLAN (no reabrir en CODE):**

- **`Bingo` pasa de `private init` a `private set` en `NombreEvento`, `FechaSorteoUtc` y
  `CostoPorCarton`**, agregando un método de instancia `Actualizar(...)` que reaplica las mismas
  validaciones que `Crear` (fecha futura, costo > 0) y lanza las mismas excepciones ya existentes
  (`FechaSorteoInvalidaException`, `CostoPorCartonInvalidoException`) — **sin excepciones 400
  nuevas**. `Id`, `OrganizadorId` y `CantidadCartones` siguen `private init`, nunca editables (fuera
  de alcance del PRD). El dominio sigue sin I/O: `Actualizar` no toca el repositorio.
- **EF-Core-friendly, no "replace":** en vez de que `Actualizar` devuelva una nueva instancia (que
  EF Core no trackearía como modificada sin trabajo extra), muta la instancia ya trackeada por el
  `DbContext` (cargada vía `ObtenerPorIdAsync`) — `SaveChangesAsync` detecta el cambio automáticamente,
  mismo mecanismo implícito que ya usa el resto del proyecto vía EF Core change tracking.
- **`IBingoRepository` gana 3 métodos, no un repositorio nuevo**: `ObtenerPorIdAsync(Guid id)` →
  `Bingo?`, `EliminarAsync(Bingo bingo)`, `TieneComprasRegistradasAsync(Guid bingoId)` → `bool`. Es
  la única implementación de este contrato en el sistema (confirmado en el impact scan) — agregar
  métodos no rompe ningún caller existente.
- **`TieneComprasRegistradasAsync` hoy devuelve `Task.FromResult(false)` explícito y comentado**,
  no un `true` hardcodeado en otra capa: es el único punto del sistema que sabrá sobre `Compra`
  cuando esa entidad exista (ticket futuro de carrito/compra), y el único método a tocar para
  activar el chequeo real — el resto de la cadena (`BingoService`, excepción, middleware) ya queda
  completo en este ticket.
- **Dos excepciones nuevas** (`BingoNoEncontradoException` 404, `BingoConComprasException` 409),
  mismo patrón que las 6 ya existentes en `Domain/Bingos/Exceptions/` (heredan de `DomainException`).
  Primer 404 del proyecto — el middleware solo necesita un `catch` adicional, sin mecanismo nuevo.
- **No distinguir "no existe" de "es de otro organizador"**: ambos casos lanzan
  `BingoNoEncontradoException` → 404 (AC-02/AC-06 del PRD, mismo criterio de no-enumeración que el
  resto del proyecto).
- **`PUT`/`DELETE` en `BingosController` existente**, no un controller nuevo — ruta
  `/api/bingos/{id}`, hereda `[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]`
  ya declarado a nivel de clase.
- **`EditarBingoRequest` con `[Required, MaxLength(200)]` en `NombreEvento` y `[Required]` en el
  resto, sin `[Range]`** — mismo criterio y misma razón que `CrearBingoRequest` (spec FEAT-003,
  Block 4): un `[Range]` en `CostoPorCarton` dispararía el 400 automático antes de que
  `Bingo.Actualizar` se ejecute, haciendo inalcanzable `CostoPorCartonInvalidoException`.
- **`PUT` responde con `BingoCreadoResponse` (ya existente)**, no un DTO nuevo — mismos 5 campos
  (`Id`, `NombreEvento`, `FechaSorteoUtc`, `CantidadCartones`, `CostoPorCarton`), `CantidadCartones`
  no cambia pero se sigue devolviendo para que el cliente no tenga que hacer un segundo `GET`.
- **`DELETE` responde 204 sin body**, patrón REST estándar, no usado todavía en el proyecto pero sin
  alternativa mejor para una eliminación exitosa.
- **Orden de checks en `BingoService`, igual en `EditarAsync` y `EliminarAsync`**: (1) cargar bingo
  por Id — si `null` → `BingoNoEncontradoException`; (2) `bingo.OrganizadorId != organizadorId` →
  `BingoNoEncontradoException` (mismo tipo que (1), no-enumeración); (3)
  `TieneComprasRegistradasAsync` → si `true`, `BingoConComprasException`; (4) ejecutar la operación.

## Block 1 — Domain: entidad editable + Infraestructura: repositorio extendido

**Files**
- `backend/BingoCart.Domain/Bingos/Bingo.cs` (modified) — `NombreEvento`, `FechaSorteoUtc`,
  `CostoPorCarton` pasan de `{ get; private init; }` a `{ get; private set; }`. Nuevo método:
  ```csharp
  public void Actualizar(string nombreEvento, DateTime fechaSorteoUtc, decimal costoPorCarton, DateTime ahoraUtc)
  {
      if (fechaSorteoUtc <= ahoraUtc)
      {
          throw new FechaSorteoInvalidaException("La fecha de sorteo debe ser futura.");
      }
      if (costoPorCarton <= 0)
      {
          throw new CostoPorCartonInvalidoException("El costo por cartón debe ser mayor a cero.");
      }
      NombreEvento = nombreEvento;
      FechaSorteoUtc = fechaSorteoUtc;
      CostoPorCarton = costoPorCarton;
  }
  ```
  Mismo orden de validación que `Crear` (fecha antes que costo). `NombreEvento` no se valida acá,
  mismo criterio que `Crear` (la validación de forma vive en el DTO de Application).
- `backend/BingoCart.Domain/Bingos/Exceptions/BingoNoEncontradoException.cs` (new) — hereda
  `DomainException`, mismo patrón que las 6 excepciones existentes.
- `backend/BingoCart.Domain/Bingos/Exceptions/BingoConComprasException.cs` (new) — ídem.
- `backend/BingoCart.Application/Bingos/IBingoRepository.cs` (modified) — agrega:
  ```csharp
  Task<Bingo?> ObtenerPorIdAsync(Guid id);
  Task EliminarAsync(Bingo bingo);
  Task<bool> TieneComprasRegistradasAsync(Guid bingoId);
  ```
- `backend/BingoCart.Infrastructure/Bingos/BingoRepository.cs` (modified) — implementa los 3:
  `ObtenerPorIdAsync` → `_context.Bingos.FindAsync(id)` (o `FirstOrDefaultAsync`, trackeado, para que
  una mutación posterior vía `Actualizar` + `SaveChangesAsync` funcione sin `Update()` explícito).
  `EliminarAsync` → `_context.Bingos.Remove(bingo); await _context.SaveChangesAsync();` (el cascade
  a `Cartones` ya está configurado en el esquema, `AppDbContext.cs:80-84` — una sola sentencia SQL).
  `TieneComprasRegistradasAsync` → `Task.FromResult(false)`, con el comentario documentado en
  "Decisiones cerradas en PLAN" como punto de extensión.
- Nota: no se agrega un `ActualizarAsync` separado al repositorio — quien llame a `Actualizar()`
  sobre una entidad obtenida vía `ObtenerPorIdAsync` (trackeada) solo necesita invocar
  `SaveChangesAsync()` (que Block 2 llama a través de un método de conveniencia, ver Block 2).

**Logic**
Capa de dominio pura para `Bingo.Actualizar` (sin I/O, mismas invariantes que `Crear`). Capa de
infraestructura pura para el repositorio (sin decisiones de negocio: qué organizador es dueño, qué es
"tiene compras" en términos de autorización — eso lo decide Application en Block 2).

**API contract**
N/A — este bloque no expone ningún endpoint.

**Data model**
Sin cambios de esquema: `NombreEvento`/`FechaSorteoUtc`/`CostoPorCarton` ya eran columnas normales;
pasar de `private init` a `private set` es un cambio de C#, no de EF Core mapping.

**Input validation**
`Actualizar` valida invariantes de dominio (fecha futura, costo > 0) — mismo criterio que `Crear`.
El repositorio no revalida nada, confía en el contrato del llamador (Application, Block 2).

**Error handling**
`Actualizar` propaga `FechaSorteoInvalidaException`/`CostoPorCartonInvalidoException` sin capturar
(mismas excepciones que `Crear`, no hay 400 nuevos). El repositorio no captura ninguna excepción.

**Required tests**
- [ ] `Bingo.Actualizar` con nombre, fecha futura y costo válidos → actualiza los 3 campos, `Id`/
  `OrganizadorId`/`CantidadCartones` no cambian — valida AC-01 (parte de dominio).
- [ ] `Bingo.Actualizar` con fecha de sorteo no futura → lanza `FechaSorteoInvalidaException`, sin
  modificar el estado del bingo — valida AC-03.
- [ ] `Bingo.Actualizar` con costo por cartón ≤ 0 → lanza `CostoPorCartonInvalidoException`, sin
  modificar el estado del bingo — valida parte de dominio de AC-01 (contraparte inválida).
- [ ] `BingoRepository.ObtenerPorIdAsync` con Id existente → devuelve el `Bingo` correcto (mismos
  campos que se persistieron) — valida AC-01/AC-02/AC-05/AC-06 (parte de infraestructura).
- [ ] `BingoRepository.ObtenerPorIdAsync` con Id inexistente → devuelve `null` — valida AC-02/AC-06
  (parte de infraestructura).
- [ ] `BingoRepository.EliminarAsync` sobre un bingo con cartones → tras la llamada, ni el bingo ni
  ninguno de sus cartones existen en la base (consulta directa a ambas tablas) — valida AC-05/FR-07.
- [ ] `BingoRepository.TieneComprasRegistradasAsync` con cualquier `bingoId` (incluido uno recién
  creado) → siempre `false` — valida el comportamiento documentado del punto de extensión.

**Completion criterion**
Los 7 tests pasan; `Bingo.Actualizar` nunca deja el estado a medio modificar si valida en el orden
correcto; `EliminarAsync` nunca deja cartones huérfanos.

## Block 2 — Application + Api: orquestación, endpoints y mapeo de errores

**Files**
- `backend/BingoCart.Application/Bingos/Dtos/EditarBingoRequest.cs` (new) — `sealed record
  EditarBingoRequest([Required, MaxLength(200)] string NombreEvento, [Required] DateTime
  FechaSorteoUtc, [Required] decimal CostoPorCarton);` — mismo estilo que `CrearBingoRequest`.
- `backend/BingoCart.Application/Bingos/IBingoService.cs` (modified) — agrega:
  ```csharp
  Task<BingoCreadoResponse> EditarAsync(Guid id, EditarBingoRequest request, Guid organizadorId);
  Task EliminarAsync(Guid id, Guid organizadorId);
  ```
- `backend/BingoCart.Application/Bingos/BingoService.cs` (modified) — implementa ambos:
  1. `var bingo = await _bingoRepository.ObtenerPorIdAsync(id);`
  2. `if (bingo is null || bingo.OrganizadorId != organizadorId) throw new
     BingoNoEncontradoException("El bingo indicado no existe.");` — mismo mensaje para ambos casos
     (no-enumeración).
  3. `var tieneCompras = await _bingoRepository.TieneComprasRegistradasAsync(id); if (tieneCompras)
     throw new BingoConComprasException("El bingo tiene compras registradas.");`
  4. **`EditarAsync`**: `var ahoraUtc = _timeProvider.GetUtcNow().UtcDateTime; bingo.Actualizar(
     request.NombreEvento, request.FechaSorteoUtc, request.CostoPorCarton, ahoraUtc); await
     _bingoRepository.GuardarCambiosAsync();` — agrega `Task GuardarCambiosAsync()` a
     `IBingoRepository`/`BingoRepository` como wrapper directo de `_context.SaveChangesAsync()`
     (necesario porque `Actualizar()` muta la instancia trackeada, no hay otro método que persista
     ese cambio). Devuelve `new BingoCreadoResponse(bingo.Id, bingo.NombreEvento,
     bingo.FechaSorteoUtc, bingo.CantidadCartones, bingo.CostoPorCarton);`.
  5. **`EliminarAsync`**: `await _bingoRepository.EliminarAsync(bingo);`
- `backend/BingoCart.Api/Controllers/BingosController.cs` (modified) — agrega:
  ```csharp
  [HttpPut("{id:guid}")]
  [ProducesResponseType(typeof(BingoCreadoResponse), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status401Unauthorized)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  public async Task<ActionResult<BingoCreadoResponse>> Editar(Guid id, [FromBody] EditarBingoRequest request)
  {
      var organizadorId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
      var response = await _bingoService.EditarAsync(id, request, organizadorId);
      return Ok(response);
  }

  [HttpDelete("{id:guid}")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType(StatusCodes.Status401Unauthorized)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  public async Task<IActionResult> Eliminar(Guid id)
  {
      var organizadorId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
      await _bingoService.EliminarAsync(id, organizadorId);
      return NoContent();
  }
  ```
  Ambos delegan 100% a `IBingoService`, sin lógica de negocio — mismo patrón que `Crear`/`Listar`.
  Sin rate limiting (mismo criterio que `Listar`: no tienen un costo análogo a generar cartones).
- `backend/BingoCart.Api/Middleware/ExceptionHandlingMiddleware.cs` (modified) — agrega:
  ```csharp
  catch (BingoNoEncontradoException ex)
  {
      await ManejarExcepcionDeDominioAsync(context, ex, HttpStatusCode.NotFound, "BingoNoEncontrado");
  }
  catch (BingoConComprasException ex)
  {
      await ManejarExcepcionDeDominioAsync(context, ex, HttpStatusCode.Conflict, "BingoConCompras");
  }
  ```

**API contract**
- `PUT /api/bingos/{id}` — body: `{ "nombreEvento": "string", "fechaSorteoUtc": "string",
  "costoPorCarton": "number" }`. Response 200: `{ "id": "guid", "nombreEvento": "string",
  "fechaSorteoUtc": "string", "cantidadCartones": "int", "costoPorCarton": "number" }`. Response
  400: fecha no futura o costo ≤ 0 (`{ "error": "FechaSorteoInvalida" | "CostoPorCartonInvalido",
  "message": "..." }`) o `DatosInvalidos` si falta un campo requerido. Response 401: sin JWT válido.
  Response 404: `{ "error": "BingoNoEncontrado", "message": "..." }` — bingo inexistente o de otro
  organizador. Response 409: `{ "error": "BingoConCompras", "message": "..." }`.
- `DELETE /api/bingos/{id}` — sin body. Response 204: sin body. Response 401/404/409: igual forma
  que `PUT`.
- Auth: JWT vía cookie `httpOnly` (heredado de `[Authorize]` a nivel de clase, mismo mecanismo que
  `Crear`/`Listar`).

**Input validation**
`[Required, MaxLength(200)]`/`[Required]` en `EditarBingoRequest` (400 automático si falta un
campo). Validación de invariantes (fecha futura, costo > 0) en `Bingo.Actualizar` (Block 1).

**Error handling**
`BingoNoEncontradoException` → 404, `BingoConComprasException` → 409 (ambas nuevas en este bloque,
capturadas en el middleware ya existente). `FechaSorteoInvalidaException`/
`CostoPorCartonInvalidoException` ya estaban mapeadas a 400 desde FEAT-003 — sin cambios ahí.

**Required tests**
- [ ] `BingoServiceTests`: `EditarAsync` con datos válidos y bingo propio sin compras → devuelve
  `BingoCreadoResponse` con los campos actualizados; verifica que `GuardarCambiosAsync` fue invocado
  — valida AC-01 (orquestación).
- [ ] `BingoServiceTests`: `EditarAsync` con Id inexistente → `BingoNoEncontradoException` — valida
  AC-02.
- [ ] `BingoServiceTests`: `EditarAsync` con bingo de otro organizador → `BingoNoEncontradoException`
  (mismo tipo que el caso anterior, verificado explícitamente) — valida AC-02 (no-enumeración).
- [ ] `BingoServiceTests`: `EditarAsync` con `TieneComprasRegistradasAsync` mockeado a `true` →
  `BingoConComprasException` — valida AC-04.
- [ ] `BingoServiceTests`: `EliminarAsync` con bingo propio sin compras → invoca
  `IBingoRepository.EliminarAsync` con el bingo correcto — valida AC-05 (orquestación).
- [ ] `BingoServiceTests`: `EliminarAsync` con Id inexistente → `BingoNoEncontradoException` — valida
  AC-06.
- [ ] `BingoServiceTests`: `EliminarAsync` con `TieneComprasRegistradasAsync` mockeado a `true` →
  `BingoConComprasException` — valida AC-07.
- [ ] `BingosControllerTests` (integración, `WebApplicationFactory` + SQL Server real): organizador
  autenticado crea un bingo, luego `PUT /api/bingos/{id}` con datos válidos → 200 con los campos
  actualizados; un `GET /api/bingos` posterior confirma la persistencia real — valida AC-01
  end-to-end.
- [ ] `BingosControllerTests`: `PUT /api/bingos/{id-inexistente}` autenticado → 404
  `BingoNoEncontrado` — valida AC-02 end-to-end.
- [ ] `BingosControllerTests`: organizador A crea un bingo, organizador B autenticado intenta
  `PUT /api/bingos/{id-de-A}` → 404 `BingoNoEncontrado` — valida AC-02 end-to-end (no-enumeración
  entre organizadores reales).
- [ ] `BingosControllerTests`: `PUT` con `fechaSorteoUtc` pasada → 400 `FechaSorteoInvalida` —
  valida AC-03 end-to-end.
- [ ] `BingosControllerTests`: `PUT` con `costoPorCarton` ≤ 0 → 400 `CostoPorCartonInvalido` —
  valida la contraparte inválida de AC-01 end-to-end (F-SPEC-16: `CostoPorCartonInvalidoException`
  está documentada como error de este bloque en "Error handling" y necesita su propio test acá, no
  solo el de Block 1 a nivel de dominio — el endpoint `PUT` en sí es nuevo).
- [ ] `BingosControllerTests`: `PUT /api/bingos/{id}` sin cookie de autenticación → 401 — valida
  AC-08 end-to-end.
- [ ] `BingosControllerTests`: organizador autenticado crea un bingo con cartones, luego
  `DELETE /api/bingos/{id}` → 204; un `GET /api/bingos` posterior confirma que el bingo ya no
  aparece, y una consulta directa a `Cartones` confirma que tampoco quedan cartones de ese bingo —
  valida AC-05/FR-07 end-to-end.
- [ ] `BingosControllerTests`: `DELETE /api/bingos/{id-inexistente}` autenticado → 404
  `BingoNoEncontrado` — valida AC-06 end-to-end.
- [ ] `BingosControllerTests`: organizador A crea un bingo, organizador B autenticado intenta
  `DELETE /api/bingos/{id-de-A}` → 404 `BingoNoEncontrado` — valida AC-06 end-to-end.
- [ ] `BingosControllerTests`: `DELETE /api/bingos/{id}` sin cookie de autenticación → 401 — valida
  AC-08 end-to-end.

**Completion criterion**
Los 17 tests pasan (7 unit en `BingoServiceTests` + 10 de integración en `BingosControllerTests`);
ningún organizador puede editar ni eliminar un bingo ajeno (verificado con dos organizadores reales,
no solo con un Id inventado); eliminar un bingo elimina también sus cartones, verificado con una
consulta directa a la tabla `Cartones` (no solo por la ausencia en el listado).

## Final verification

`dotnet build`/`dotnet test` sobre `backend/BingoCart.sln` en verde, incluyendo los 24 tests
automatizados nuevos de los Blocks 1-2 (7+17). Un organizador autenticado puede editar y eliminar sus
propios bingos sin compras registradas; cualquier intento sobre un bingo ajeno o inexistente devuelve
404 sin distinguir el caso; eliminar un bingo elimina también todos sus cartones en la misma
operación. Ningún frontend se toca en este ticket (confirmado backend-only por el PRD, mismo criterio
que FEAT-003/FEAT-004/FEAT-005).
