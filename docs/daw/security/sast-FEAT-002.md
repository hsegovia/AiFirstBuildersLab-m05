# SAST FEAT-002: Reordenar directorios backend bajo backend/

| Field | Value |
|-------|-------|
| Ticket | FEAT-002 |
| Scope | diff (movimiento de directorios + configuración Docker) |
| Date | 2026-08-16 |
| Result | PASSED |

## Secrets (F-SAST-01)

✅ `docker-compose.yml`, `backend/BingoCart.Api/Dockerfile`, `backend/.dockerignore` (nuevo) — sin
secretos hardcodeados. `.env` está en `.gitignore` (línea 19). `backend/.dockerignore` agrega
`appsettings.*.local.json` como control preventivo adicional, mitigación del riesgo TM-01
documentado en `docs/daw/security/threat-FEAT-002.md`.

## Injection / XSS / funciones inseguras / crypto débil / SSRF / debug mode / logging sensible /
## upload / CSRF / validación de input / manejo de errores

N/A — el diff de este ticket no toca código de aplicación (controllers, servicios, queries,
componentes de frontend). Es movimiento de archivos (`git mv`) más ajuste de rutas relativas en
`.sln`/`.csproj`, y configuración de build de Docker (`docker-compose.yml`, `Dockerfile`,
`.dockerignore`).

## Dependencias (F-SAST-13/16)

✅ Sin paquetes NuGet ni npm nuevos en este ticket. Los `.csproj` modificados solo cambian rutas
relativas de `ProjectReference` existentes, ninguna versión de paquete.

## Observación fuera de scope (no bloquea)

`backend/BingoCart.Api/appsettings.Development.json` contiene una password de SQL Server de
desarrollo, trackeada en git desde FEAT-001a (ya señalada por `daw-arch-auditor` en la revisión de
Block 2). Este ticket solo reubica el archivo vía `git mv`, no cambia su contenido — no es un
finding nuevo de FEAT-002. Queda anotada para un eventual ticket de hardening, no se suprime acá
porque no es un finding de este scan.

## Suppressions

Ninguna — 0 findings Medium o superiores.

## Result

Total: 3 clean, 0 vulnerabilidades (0 Critical, 0 High, 0 Medium sin suprimir). **PASSED.**
