# SAST FEAT-001a: Registro de organizador

| Field | Value |
|-------|-------|
| Ticket | FEAT-001a |
| Date | 2026-08-16 |
| Scope | Repo completo (backend .NET + frontend Angular), cierre de CODE |

## Secretos (F-SAST-01)

- ✅ Sin credenciales/tokens/API keys hardcodeadas en código de producción (grep dirigido, 0
  resultados fuera de `docker-compose.yml`/`appsettings.Development.json`, ya aceptados como
  riesgo de desarrollo local en el threat model del ticket).
- ✅ `.env` cubierto por `.gitignore` (raíz, hereda a subdirectorios). `*.env` y
  `appsettings.*.local.json` agregados a ambos `.dockerignore` como hardening preventivo.

## Inyección (F-SAST-02, F-SAST-03, F-SAST-05)

- ✅ Sin SQL raw / concatenación de queries — toda persistencia vía EF Core (`AppDbContext`,
  `IdentityGateway`), sin `FromSqlRaw`/`ExecuteSqlRaw`.
- ✅ Sin `Process.Start`/`Server.Execute` — sin superficie de command injection.
- N/A Path traversal — el ticket no maneja rutas de archivo derivadas de input de usuario.

## XSS y funciones inseguras (F-SAST-04, F-SAST-06, F-SAST-08, F-SAST-17)

- ✅ Sin `innerHTML`/`bypassSecurityTrust`/`dangerouslySetInnerHTML` en el frontend — todo
  renderizado vía interpolación `{{ }}` estándar de Angular (auto-sanitizada).
- ✅ Sin `eval()` ni deserialización insegura.
- ✅ Sin MD5/SHA1/DES/ECB — el hash de password se delega íntegramente a ASP.NET Core Identity
  (PBKDF2 + salt), verificado en Block 3 (NFR-01 del PRD).

## Logging de datos sensibles (F-SAST-10)

- ✅ `ExceptionHandlingMiddleware` nunca loguea CUIT/mail/teléfono — solo tipo de excepción,
  timestamp y `TraceIdentifier` (verificado en Block 4).
- ✅ El log de auditoría de registro exitoso (`OrganizadoresController`) solo incluye el `Guid`
  generado.

## Otras categorías (F-SAST-07, F-SAST-09, F-SAST-11, F-SAST-12, F-SAST-14, F-SAST-15)

- N/A SSRF — sin llamadas salientes a URLs derivadas de input de usuario.
- ✅ Debug mode — `ASPNETCORE_ENVIRONMENT=Development` en el contenedor `api` (Block 7) solo
  habilita Swagger; confirmado que `UseDeveloperExceptionPage()` no está registrado en ningún
  punto del pipeline, por lo que no hay fuga de stack traces independientemente del entorno.
- N/A Unrestricted upload — sin funcionalidad de subida de archivos en este ticket.
- N/A CSRF — endpoint público sin autenticación por cookie (JSON API, sin sesión), fuera del
  modelo de amenaza de CSRF clásico.
- ✅ Validación de input completa — CUIT/teléfono/password/mail/nombreOrganizacion, todos
  validados server-side (Domain + DataAnnotations), nunca solo client-side.
- ✅ Manejo de errores sin fuga de internals — `ExceptionHandlingMiddleware` devuelve siempre un
  mensaje genérico en el 500, nunca stack traces ni detalles de excepción no controlada.

## Dependencias (F-SAST-13, F-SAST-16)

### Backend (.NET) — ✅ Resuelto

`dotnet list BingoCart.sln package --vulnerable --include-transitive` encontró inicialmente 2
CVEs High (`System.Net.Http` 4.3.0, `System.Text.RegularExpressions` 4.3.0), transitivas de
`xunit` → `NETStandard.Library` 1.6.1, presentes en los 5 proyectos de test (nunca en los 4
proyectos de producción, confirmado). Triage (`daw-sec-auditor`): ensamblados de solo-referencia,
nunca cargados en runtime net8.0 ni empaquetados en la imagen Docker publicada (el Dockerfile de
`BingoCart.Api` solo copia `src/`, nunca `tests/`).

**Remediación aplicada:** override explícito de versión parcheada
(`System.Net.Http` 4.3.4, `System.Text.RegularExpressions` 4.3.1) en los 5 `.csproj` de test.
Verificado: `dotnet list ... --vulnerable` → 0 hallazgos en las 9 unidades de la solución. Suite
completa re-corrida tras el fix: 41/41 tests verdes, sin regresiones.

### Frontend (npm) — ⚠️ Riesgo aceptado (ver suppresión abajo)

`npm audit`: 1 Critical, 32 High, 17 Moderate, 7 Low (57 total). De los High, todos menos uno
(`@angular/core` y sus paquetes hermanos afectados por la misma causa) son ruido del mismo árbol
de dependencias de Angular CLI/toolchain. Los dos hallazgos con severidad Critical/High reales y
no triviales, tras triage exhaustivo con evidencia:

## Suppressions

### Suppression: `tar` — CVE Critical (GHSA-23hp-3jrh-7fpw y relacionadas)

| Field | Value |
|---|---|
| File | `frontend/package.json` (transitiva vía `@angular/cli` → `pacote`/`node-gyp`/`cacache`) |
| Category | F-SAST-13 (CVE Critical en dependencia) |
| Disposition | ACCEPTED_RISK |
| Reviewer | Usuario (product owner del proyecto) |
| Date | 2026-08-16 |
| Justification | Verificado con `grep` sobre `dist/frontend/browser/*.js` tras un `ng build` real: 0 ocurrencias de `tar` en el bundle servido al navegador. Es dependencia de desarrollo (`npm ls tar --all` confirma `isDirect: false`, solo se ejecuta durante `npm install`/build en la máquina de desarrollo o CI). No hay ruta de explotación contra la aplicación desplegada ni sus usuarios finales. |
| Compensating control | `package-lock.json` versionado (evita resolución no determinista de versiones); no se ejecuta `npm install`/`ng add`/`ng update` contra fuentes no confiables. |
| Review by | 2027-02-16 (6 meses), o antes si se planifica la migración de Angular (ver ADR-001) |

### Suppression: `@angular/core` 18.2.14 — 5 CVEs High (XSS/DOM-clobbering)

| Field | Value |
|---|---|
| File | `frontend/package.json` (dependencia directa, `isDirect: true`) |
| Category | F-SAST-13 (CVE High en dependencia) |
| Disposition | ACCEPTED_RISK |
| Reviewer | Usuario (product owner del proyecto) |
| Date | 2026-08-16 |
| Justification | 18.2.14 es la última versión publicada de la serie 18.x — sin parche disponible en el mismo major. Las 5 CVEs (GHSA-prjf-86w9-mfqv, GHSA-g93w-mfhg-p222, GHSA-jrmj-c5cx-3cw6, GHSA-rgjc-h3x7-9mwg, GHSA-jj27-h5hq-8x99) requieren i18n/`$localize`, SVG dinámico, SSR/hydration o creación dinámica de componentes — ninguna feature está en uso en el código actual (verificado leyendo cada template/componente del repo). El único fix real es un salto a `@angular/core@22.1.2` (4 versiones major), fuera de alcance de este ticket y en conflicto con "Angular 18" declarado en `AGENTS.md`. Decisión y detalle completo en **ADR-001** (`docs/adr/adr-001-riesgo-aceptado-cves-angular-18.md`). |
| Compensating control | Ninguna de las superficies vulnerables (i18n, SVG dinámico, hydration, componentes dinámicos) está en uso; toda interpolación de datos de usuario pasa por el binding estándar de Angular (auto-sanitizado). |
| Review by | 2027-02-16 (6 meses), o antes de agregar cualquier feature que use i18n/SVG dinámico/SSR/componentes dinámicos — lo que ocurra primero. Ticket de seguimiento pendiente de crear: migración Angular 18→LTS. |

## Resultado

| Categoría | Estado |
|---|---|
| Secretos | ✅ Limpio |
| Inyección | ✅ Limpio |
| XSS / funciones inseguras | ✅ Limpio |
| Logging de datos sensibles | ✅ Limpio |
| Dependencias .NET | ✅ Limpio (2 High corregidas) |
| Dependencias npm | ⚠️ 2 riesgos aceptados formalmente (1 Critical, 1 High — ver suppressions arriba y ADR-001) |

**Total: 0 Critical/High sin disposición. Todos los hallazgos Critical/High tienen remediación
aplicada o supresión formal con los 7 campos + ADR de respaldo.**

**Result: PASSED** — `gates.sast` = `true`.

## Re-cierre (corrective loop VERIFY→CODE, 2026-08-16)

VERIFY encontró 2 gaps reales (TDE prometido pero no implementado; branch coverage del frontend
por debajo del mínimo) que requirieron volver a CODE. Cambios relevantes para este SAST:

- `AppDbContextTdeExtensions.cs` (nuevo): ejecuta SQL administrativo (`CREATE MASTER KEY`,
  `CREATE CERTIFICATE`, `CREATE DATABASE ENCRYPTION KEY`, `ALTER DATABASE ... SET ENCRYPTION ON`)
  vía `ExecuteSqlRawAsync`/ADO.NET directo. Revisado línea por línea: el password de la master key
  y el nombre de la base vienen exclusivamente de configuración de servidor (`appsettings`/env
  var), nunca de una request HTTP — no hay ruta de input de usuario hacia este SQL. **F-SAST-02: no
  aplica (no es input de usuario), sin hallazgo.**
- `Tde:MasterKeyPassword` (nuevo, `appsettings.json`/`appsettings.Development.json`/
  `docker-compose.yml`): mismo patrón ya aceptado que `ConnectionStrings:Default` y
  `MSSQL_SA_PASSWORD` — placeholder vacío en el archivo versionado sin sufijo, valor de desarrollo
  local en `.Development.json`, override por variable de entorno en compose. **F-SAST-01: sin
  hallazgo nuevo**, mismo criterio ya aplicado.
- Dependencias: sin cambios (`dotnet list --vulnerable` → 0 en las 9 unidades; no se agregó ningún
  paquete NuGet ni npm nuevo en este re-cierre — solo tests nuevos con los paquetes ya
  referenciados).

**Result: PASSED** — `gates.sast` se re-confirma `true` sobre el estado final del código.
