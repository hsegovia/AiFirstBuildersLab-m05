# SAST Report — FEAT-003 (Crear bingo con generación de cartones)

Fecha: 2026-08-18
Ticket: FEAT-003
Alcance: `backend/BingoCart.Domain/Bingos/`, `backend/BingoCart.Application/Bingos/`,
`backend/BingoCart.Infrastructure/Bingos/`, `backend/BingoCart.Infrastructure/Data/AppDbContext.cs`
(cambios de Bingo/Carton), `backend/BingoCart.Api/Controllers/BingosController.cs`,
`backend/BingoCart.Api/Middleware/ExceptionHandlingMiddleware.cs` (mapeos nuevos),
`backend/BingoCart.Api/Program.cs` (registro de servicios + política de rate limiting `"bingos"`).

## Secretos (F-SAST-01)
✅ Sin patrones de password/apikey/secret/connection string hardcodeados en el código auditado
(grep sobre los 4 tipos, 0 matches).

## Inyección
✅ F-SAST-02 (SQL/NoSQL): `BingoRepository` usa exclusivamente LINQ contra `AppDbContext`
(`AnyAsync`, `Add`, `AddRange`) — cero `FromSqlRaw`/`ExecuteSqlRaw`/concatenación de queries.
✅ F-SAST-03 (Command injection): sin `Process.Start` ni invocaciones a shell en el código
auditado.
✅ F-SAST-05 (Path traversal): sin manejo de rutas de archivo derivadas de input de usuario en
este ticket.

## XSS y funciones inseguras
✅ F-SAST-06: N/A — este ticket es backend-only, sin renderizado HTML.
✅ F-SAST-04/17: sin `eval`, sin deserialización insegura (`BinaryFormatter`, etc.).
✅ F-SAST-08 (crypto débil): `CartonNumberGenerator` usa exclusivamente
`System.Security.Cryptography.RandomNumberGenerator` (CSPRNG) — cero apariciones de `System.Random`
o `new Random(` en el código auditado (grep, 0 matches), satisface NFR-02 del PRD.

## Resto de categorías obligatorias
✅ F-SAST-07 (SSRF): sin llamadas salientes a URLs derivadas de input de usuario.
✅ F-SAST-09 (debug en producción): sin flags de debug agregados por este ticket.
✅ F-SAST-10 (logging de datos sensibles): `ExceptionHandlingMiddleware` — los 5 mapeos nuevos
(`FechaSorteoInvalida`, `CantidadCartonesExcedeLimite`, `CantidadCartonesInvalida`,
`CostoPorCartonInvalido`, `BingoActivoExistente`) reutilizan `ManejarExcepcionDeDominioAsync`, que
solo loguea tipo de excepción + `CorrelationId` (`TraceIdentifier`) — nunca datos de negocio
(nombre de evento, fecha de sorteo, costo).
✅ F-SAST-11 (upload sin restricción): N/A, sin endpoints de upload en este ticket.
✅ F-SAST-12 (CSRF): mismo mecanismo ya vigente de JWT vía cookie httpOnly + `SameSite`
(FEAT-001b), sin cambios en este ticket.
✅ F-SAST-14 (validación de input incompleta): `CrearBingoRequest` valida `[Required]`/
`[MaxLength(200)]` como primera barrera; `Bingo.Crear`/`Carton.Crear` (Domain) son la fuente de
verdad para los rangos de negocio (fecha futura, 1-5000 cartones, costo > 0, 10 números 1-90) —
decisión explícita del spec para no bloquear las excepciones de dominio con `[Range]` antes de que
se ejecuten.
✅ F-SAST-15 (error handling que filtra internos): el catch genérico de
`ExceptionHandlingMiddleware` nunca expone `ex.Message` real al cliente en el 500 — solo un mensaje
fijo genérico; los mensajes de las excepciones de dominio (400/409) son mensajes de negocio fijos,
sin detalles de stack ni de infraestructura.

## Dependencias (F-SAST-13/16)
✅ Cero paquetes NuGet nuevos agregados en el alcance de FEAT-003 (`git diff` de `.csproj` entre el
commit del PRD y HEAD: sin cambios).
✅ `dotnet list BingoCart.sln package --vulnerable --include-transitive`: 0 paquetes vulnerables en
los 9 proyectos de la solución.

## Suppressions
Ninguna — no hubo hallazgos Medium que requirieran supresión documentada.

---

**Total: 0 vulnerabilidades (0 Critical, 0 High, 0 Medium). 0 warnings Low/Informational nuevos.**

**Veredicto: PASSED** → `gates.sast = true`.
