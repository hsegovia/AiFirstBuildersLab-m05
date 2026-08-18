# SAST Report — FEAT-004 (Listar bingos propios del organizador)

Fecha: 2026-08-18
Ticket: FEAT-004
Alcance: `backend/BingoCart.Application/Bingos/BingosPaginados.cs`,
`backend/BingoCart.Application/Bingos/IBingoRepository.cs` (método nuevo),
`backend/BingoCart.Infrastructure/Bingos/BingoRepository.cs` (método nuevo),
`backend/BingoCart.Application/Bingos/Dtos/{ListarBingosQuery.cs,BingoListadoResponse.cs}`,
`backend/BingoCart.Application/Bingos/IBingoService.cs` (método nuevo),
`backend/BingoCart.Application/Bingos/BingoService.cs` (método nuevo),
`backend/BingoCart.Api/Controllers/BingosController.cs` (endpoint nuevo).

## Secretos (F-SAST-01)
✅ Sin patrones de password/apikey/secret/connection string hardcodeados (grep sobre los 4 tipos,
0 matches en el alcance de este ticket).

## Inyección
✅ F-SAST-02 (SQL/NoSQL): `BingoRepository.ListarPorOrganizadorAsync` usa exclusivamente LINQ contra
`AppDbContext` (`Where`, `CountAsync`, `OrderByDescending`, `Skip`/`Take`) — cero `FromSqlRaw`/
`ExecuteSqlRaw`/concatenación de queries.
✅ F-SAST-03 (Command injection): sin `Process.Start` ni invocaciones a shell.
✅ F-SAST-05 (Path traversal): sin manejo de rutas de archivo derivadas de input de usuario.

## Control de acceso / IDOR (relevante para este ticket — ver threat model R-01)
✅ `organizadorId` en `BingosController.Listar` (línea 69) se deriva exclusivamente del claim
`ClaimTypes.NameIdentifier` del JWT ya validado por `[Authorize]` — nunca de `page`/`pageSize` ni de
ningún otro parámetro de la request. Confirmado además por el test end-to-end de aislamiento
cross-organizador (`Listar_ConBingoDeOtroOrganizador_...`).

## XSS y funciones inseguras
✅ F-SAST-06: N/A — backend-only, sin renderizado HTML.
✅ F-SAST-04/17: sin `eval`, sin deserialización insegura.
✅ F-SAST-08 (crypto débil): N/A — este ticket no introduce criptografía.

## Resto de categorías obligatorias
✅ F-SAST-07 (SSRF): N/A — sin llamadas salientes.
✅ F-SAST-09 (debug en producción): sin flags de debug agregados.
✅ F-SAST-10 (logging de datos sensibles): ningún `_logger.*` nuevo en el alcance de este ticket —
`BingoService.ListarPropiosAsync` y `BingosController.Listar` no loguean nada (mismo criterio que
`Crear`, que tampoco loguea el payload de negocio).
✅ F-SAST-11 (upload sin restricción): N/A.
✅ F-SAST-12 (CSRF): mismo mecanismo ya vigente de JWT vía cookie httpOnly, sin cambios.
✅ F-SAST-14 (validación de input incompleta): `ListarBingosQuery` valida `Page`/`PageSize` con
`[Range(1, int.MaxValue)]` como primera barrera (400 automático); `BingoService` clampea `pageSize`
a 100 como defensa en profundidad adicional — dos capas de validación, no una.
✅ F-SAST-15 (error handling que filtra internos): este bloque no agrega ningún catch nuevo; los
errores de `page`/`pageSize` inválidos los maneja el pipeline de `[ApiController]` ya auditado en
FEAT-003 (mensaje fijo, sin stack trace ni detalles internos).

## Dependencias (F-SAST-13/16)
✅ Cero paquetes NuGet nuevos agregados en el alcance de FEAT-004 (`git diff` de `.csproj` entre el
último commit de FEAT-003 y HEAD: sin cambios).
✅ `dotnet list BingoCart.sln package --vulnerable --include-transitive`: 0 paquetes vulnerables en
los 9 proyectos de la solución.

## Suppressions
Ninguna — no hubo hallazgos Medium que requirieran supresión documentada.

---

**Total: 0 vulnerabilidades (0 Critical, 0 High, 0 Medium). 0 warnings Low/Informational nuevos.**

**Veredicto: PASSED** → `gates.sast = true`.
