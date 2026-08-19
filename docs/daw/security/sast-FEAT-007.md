# SAST Report — FEAT-007 (Editar y eliminar bingo sin compras)

| Field | Value |
|-------|-------|
| Ticket | FEAT-007 |
| Date | 2026-08-19 |
| Scope | Block 1 (Domain: `Bingo.Actualizar` + excepciones; Infraestructura: `BingoRepository` extendido) + Block 2 (Application: `BingoService.EditarAsync`/`EliminarAsync`; Api: `PUT`/`DELETE /api/bingos/{id}`; middleware) |

## Secrets (F-SAST-01)
✅ Sin API keys, passwords, tokens ni connection strings hardcodeados en los archivos nuevos/modificados.

## Injection

- ✅ F-SAST-02 (SQL/NoSQL injection): `BingoRepository.ObtenerPorIdAsync`/`EliminarAsync` usan
  exclusivamente LINQ sobre `AppDbContext` (`FirstOrDefaultAsync`, `Remove`), sin SQL crudo ni
  concatenación de strings. El `id` de ruta llega tipado (`Guid`, con `[HttpPut("{id:guid}")]`/
  `[HttpDelete("{id:guid}")]` — el route constraint rechaza cualquier valor no-Guid antes de llegar
  al action).
- ✅ F-SAST-03 (command injection): sin `exec`/`spawn`/`Process.Start` en el diff.
- ✅ F-SAST-05 (path traversal): no aplica, sin manejo de archivos.

## Autorización (OWASP API1:2023 — Broken Object-Level Authorization)

- ✅ **IDOR mitigado por diseño**: `BingoService.ObtenerBingoPropioSinComprasAsync` (método
  compartido de `EditarAsync`/`EliminarAsync`) compara `bingo.OrganizadorId` contra el
  `organizadorId` derivado del JWT ANTES de cualquier mutación/eliminación. Un organizador no puede
  editar ni eliminar un bingo ajeno — verificado con tests e2e usando dos organizadores reales
  registrados y logueados (no un Id inventado).
- ✅ **No-enumeración**: "no existe" y "existe pero es de otro organizador" devuelven el mismo tipo
  de excepción (`BingoNoEncontradoException`) con el mismo mensaje — un atacante no puede distinguir
  ambos casos por la respuesta.
- ✅ `organizadorId` derivado exclusivamente de `ClaimTypes.NameIdentifier` del JWT en ambos
  endpoints nuevos, nunca del `{id}` de ruta ni del body (mismo patrón que `Crear`/`Listar`).

## XSS y funciones inseguras

- ✅ F-SAST-06 (XSS): backend-only, sin render HTML.
- ✅ F-SAST-04/17: sin `eval()` ni deserialización custom — `EditarBingoRequest` usa el model binder
  estándar de ASP.NET Core con `[Required]`/`[MaxLength]`.
- ✅ F-SAST-08 (crypto débil): no aplica, sin criptografía en este ticket.

## Resto de categorías obligatorias

- ✅ F-SAST-07 (SSRF): no aplica.
- ✅ F-SAST-09 (debug mode): sin cambios de configuración de entorno.
- ✅ F-SAST-10 (logging de datos sensibles): el middleware solo loguea tipo de excepción + 
  `TraceIdentifier` (patrón ya existente), nunca datos del bingo ni del organizador.
- ✅ F-SAST-11 (upload sin restricciones): no aplica.
- ✅ F-SAST-12 (CSRF): `PUT`/`DELETE` requieren JWT válido (cookie `httpOnly`/`SameSite=Strict`, ya
  mitigado desde FEAT-001b) — mismo criterio que el resto de los endpoints autenticados.
- ✅ F-SAST-14 (validación de input incompleta): `[Required, MaxLength(200)]`/`[Required]` en
  `EditarBingoRequest`; invariantes de negocio (fecha futura, costo > 0) validadas en
  `Bingo.Actualizar` (Domain) antes de persistir — mismo patrón que `Crear`.
- ✅ F-SAST-15 (error handling que filtra internals): las 2 excepciones nuevas devuelven mensajes de
  dominio controlados (`"El bingo indicado no existe."`, `"El bingo tiene compras registradas."`),
  nunca detalles de infraestructura ni stack traces — mismo middleware ya auditado en tickets
  anteriores.

## Dependencias (F-SAST-13/16)
✅ Sin paquetes NuGet nuevos en este ticket. `dotnet list package --vulnerable` sobre las 9 proyectos
de la solución: 0 vulnerabilidades conocidas.

## Riesgos del threat model (verificación de mitigaciones)
- ✅ R-01 (HIGH, IDOR): mitigado — ver sección "Autorización" arriba, verificado con test e2e real.
- ✅ R-02 (MEDIUM, enumeración): mitigado — mismo tipo/mensaje de excepción en ambos casos.
- Los 3 riesgos LOW (sin rate limit, sin auditoría, condición de carrera) quedaron como *accepted
  risk* en el threat model — no requieren mitigación de código.

## Suppressions
Ninguna. 0 hallazgos Medium/Low que requieran documentación de supresión.

---

**Total: 0 vulnerabilidades (0 Critical, 0 High, 0 Medium, 0 Low)**
**Veredicto: PASSED**
