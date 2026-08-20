# SAST Report — FEAT-005 (Directorio público de organizadores)

| Field | Value |
|-------|-------|
| Ticket | FEAT-005 |
| Date | 2026-08-19 |
| Scope | Block 1 (índice + `DirectorioRepository`) + Block 2 (Application + Api: endpoint `GET /api/organizadores/directorio`) |

## Secrets (F-SAST-01)
✅ Sin API keys, passwords, tokens ni connection strings hardcodeados en los archivos nuevos/modificados. `.env` ya está en `.gitignore` (sin cambios en este ticket).

## Injection

- ✅ F-SAST-02 (SQL/NoSQL injection): `DirectorioRepository.ListarActivosAsync` usa exclusivamente LINQ sobre `AppDbContext` (`Join`/`Where`/`OrderBy`/`Skip`/`Take`/`Select`), sin concatenación de strings ni SQL crudo. `page`/`pageSize` llegan como `int` tipado (nunca string interpolado en query).
- ✅ F-SAST-03 (command injection): no hay `exec`/`spawn`/`Process.Start` en el diff.
- ✅ F-SAST-05 (path traversal): no hay manejo de rutas de archivo en este ticket.

## XSS y funciones inseguras

- ✅ F-SAST-06 (XSS): backend-only, sin render HTML ni `innerHTML`/`dangerouslySetInnerHTML` (confirmado: sin cambios de frontend).
- ✅ F-SAST-04/17 (eval/deserialización insegura): no aplica, sin `eval()` ni deserialización custom — el binding de `ListarDirectorioQuery` usa el model binder estándar de ASP.NET Core con `[Range]`.
- ✅ F-SAST-08 (crypto débil): no aplica, sin criptografía en este ticket.

## Resto de categorías obligatorias

- ✅ F-SAST-07 (SSRF): no aplica, sin llamadas salientes a URLs controladas por el usuario.
- ✅ F-SAST-09 (debug mode): sin cambios en configuración de entorno/`ASPNETCORE_ENVIRONMENT`.
- ✅ F-SAST-10 (logging de datos sensibles): sin `ILogger` nuevo en el diff; `DirectorioOrganizadorItem` está estructuralmente limitado a 3 campos (nunca CUIT/mail/teléfono), confirmado por test de integración que inspecciona el body crudo (AC-08/NFR-02).
- ✅ F-SAST-11 (upload sin restricciones): no aplica, sin manejo de archivos.
- ✅ F-SAST-12 (CSRF): endpoint `GET` idempotente, público, sin efectos de escritura — no requiere protección CSRF (mismo criterio que el resto de los `GET` del proyecto).
- ✅ F-SAST-14 (validación de input incompleta): `[Range(1, int.MaxValue)]` en `Page`/`PageSize` rechaza ≤0 con 400 automático; `pageSize` sin techo se clampea explícitamente a 100 en `OrganizadorService.ListarDirectorioAsync` (defensa en profundidad ante R-02 del threat model) — no depende únicamente de la validación de modelo.
- ✅ F-SAST-15 (error handling que filtra internals): sin catch nuevo; el 400 lo maneja `InvalidModelStateResponseFactory` ya existente (mensaje genérico, sin stack trace ni detalle de infraestructura), el 429 lo maneja el middleware de rate limiting ya existente.

## Dependencias (F-SAST-13/16)
✅ Sin paquetes NuGet nuevos en este ticket (sin cambios en ningún `.csproj`). `dotnet list package --vulnerable` sobre las 9 proyectos de la solución: 0 vulnerabilidades conocidas.

## Rate limiting / exposición de datos (mitigaciones del threat model)
- ✅ R-01 (exposición de CUIT/mail/teléfono): mitigado estructuralmente — `DirectorioOrganizadorItem` solo tiene 3 campos, `DirectorioRepository` nunca proyecta `ApplicationUser` completo. Verificado con test de integración sobre el body crudo (`ReadAsStringAsync` + `DoesNotContain`).
- ✅ R-02 (spam/DoS sobre endpoint público sin autenticación): mitigado con rate limiting `"directorio"` (30 req/5 min por IP), verificado con test de integración real (31ª request → 429).

## Suppressions
Ninguna. 0 hallazgos Medium/Low que requieran documentación de supresión.

---

**Total: 0 vulnerabilidades (0 Critical, 0 High, 0 Medium, 0 Low)**
**Veredicto: PASSED**
