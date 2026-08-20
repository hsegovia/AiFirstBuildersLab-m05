# SAST Report — FEAT-008a (Descubrimiento de cartones)

| Field | Value |
|-------|-------|
| Ticket | FEAT-008a |
| Date | 2026-08-20 |
| Scope | Block 1 (Infraestructura: `DescubrimientoRepository` + extensión de `DirectorioOrganizadorItem`) + Block 2 (Application: `DescubrimientoService`; Api: `CartonesController`, middleware, rate limiting) |

## Secrets (F-SAST-01)
✅ Sin API keys, passwords, tokens ni connection strings hardcodeados en los archivos nuevos/modificados.

## Injection — foco especial: primer SQL crudo del proyecto

- ✅ **F-SAST-02 (SQL injection) — revisión línea por línea de `DescubrimientoRepository.cs`**:
  `ObtenerAleatoriosGlobalAsync`/`ObtenerAleatoriosDeBingoAsync` usan `FromSqlInterpolated` (no
  `FromSqlRaw`, no concatenación de string). Los 3 huecos interpolados (`{cantidad}`, `{ahoraUtc}`,
  `{bingoId}`) se convierten automáticamente en parámetros SQL (`sp_executesql` con `@p0`/`@p1`,
  confirmado por el module-verifier de CODE) — nunca en texto embebido en la sentencia. Origen de
  cada valor: `cantidad` es siempre la constante interna `CantidadPorTanda = 5` (Application, nunca
  un parámetro de request); `ahoraUtc` viene de `TimeProvider`, no de input; `bingoId` es un `Guid`
  ya tipado por el routing (`{organizadorId:guid}`) o devuelto por una consulta previa del propio
  repositorio — en ningún caso una string cruda del usuario llega a la interpolación. El resto del
  repositorio (`ExisteOrganizadorAsync`, `ObtenerBingoActivoDeOrganizadorAsync`,
  `ObtenerResumenBingosAsync`) es LINQ puro, sin SQL crudo.
- ✅ F-SAST-03 (command injection): sin `exec`/`spawn`/`Process.Start`.
- ✅ F-SAST-05 (path traversal): no aplica, sin manejo de archivos.

## Autorización / exposición de datos

- ✅ `organizadorId` en `GET /api/cartones/organizador/{organizadorId}` se valida contra
  `ExisteOrganizadorAsync` antes de cualquier consulta downstream — no es una fuente de injection ni
  de IDOR (endpoint público de solo lectura, sin ninguna acción de escritura posible).
- ✅ `DirectorioOrganizadorItem.Id` (extensión de FEAT-005 en este ticket) verificado con test
  dedicado: no reabre la exposición de CUIT/mail/teléfono (mitigación R-01 de FEAT-005, test de
  inspección estructural por reflection sigue pasando con exactamente 4 propiedades permitidas).
- ✅ `CartonDescubiertoResponse`/`GET /api/cartones/organizador/{id}` verificado con test de
  inspección de body crudo: CUIT/mail/teléfono del organizador no aparecen en la respuesta.

## XSS y funciones inseguras

- ✅ F-SAST-06 (XSS): backend-only, sin render HTML.
- ✅ F-SAST-04/17: sin `eval()` ni deserialización custom.
- ✅ F-SAST-08 (crypto débil): `NEWID()` no es CSPRNG, pero no se usa con fines de seguridad (ver
  threat model, R-04 accepted risk — es solo variedad de UX en qué cartones ya existentes se
  muestran, no generación de números de cartón, que sigue usando `RandomNumberGenerator` desde
  FEAT-003, sin cambios en este ticket).

## Resto de categorías obligatorias

- ✅ F-SAST-07 (SSRF): no aplica.
- ✅ F-SAST-09 (debug mode): sin cambios de configuración de entorno.
- ✅ F-SAST-10 (logging de datos sensibles): el middleware solo loguea tipo de excepción +
  `TraceIdentifier` (patrón ya existente).
- ✅ F-SAST-11 (upload sin restricciones): no aplica.
- ✅ F-SAST-12 (CSRF): ambos endpoints son `GET` idempotentes, sin autenticación ni efectos de
  escritura — no aplica protección CSRF.
- ✅ F-SAST-14 (validación de input incompleta): `organizadorId:guid` rechaza automáticamente
  valores no-Guid (404 de routing); sin más input que validar (el endpoint global no recibe
  parámetros).
- ✅ F-SAST-15 (error handling que filtra internals): `OrganizadorNoEncontradoException` devuelve
  un mensaje de dominio controlado (`"El organizador indicado no existe."`), nunca detalles de
  infraestructura.

## Dependencias (F-SAST-13/16)
✅ Sin paquetes NuGet nuevos. `dotnet list package --vulnerable`: 0 vulnerabilidades conocidas en
las 9 proyectos de la solución.

## Riesgos del threat model (verificación de mitigaciones)
- ✅ R-01 (MEDIUM, SQL injection vía SQL crudo): mitigado — confirmado arriba, línea por línea.
- ✅ R-02 (MEDIUM, exposición de `organizadorId`): mitigado — es un identificador de recurso, no
  habilita ninguna acción nueva, no reabre la protección de datos de contacto.
- Los 2 riesgos LOW (scraping del catálogo, `NEWID()` no criptográfico) quedaron como *accepted
  risk* en el threat model — no requieren mitigación de código.

## Suppressions
Ninguna. 0 hallazgos Medium/Low que requieran documentación de supresión.

---

**Total: 0 vulnerabilidades (0 Critical, 0 High, 0 Medium, 0 Low)**
**Veredicto: PASSED**
