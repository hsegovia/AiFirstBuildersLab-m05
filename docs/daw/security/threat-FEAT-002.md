# Threat Model FEAT-002: Reordenar directorios backend bajo backend/

| Field | Value |
|-------|-------|
| Ticket | FEAT-002 |
| Spec | docs/daw/specs/spec-FEAT-002.md |
| Date | 2026-08-16 |
| Result | PASSED (tras mitigación folded en spec) |

## Attack surfaces identified

1. **Contexto de build Docker del servicio `api`** — `docker-compose.yml`, `backend/BingoCart.Api/
   Dockerfile`, `backend/.dockerignore` (todos modificados/creados en Block 2 del spec).

## Trust boundaries declared

- Filesystem del host/CI → contexto de build Docker (`backend/`) → capa de imagen del contenedor
  `api`. Boundary preexistente (hoy `context: .`), redefinido a `context: ./backend` en Block 2.

## STRIDE — Contexto de build Docker (`api`)

| Categoría | Análisis |
|---|---|
| Spoofing | N/A — sin cambio de identidad ni autenticación |
| Tampering | N/A — build estático, sin input dinámico |
| Repudiation | N/A — sin cambio de logging |
| Information Disclosure | 🟠 HIGH (ver Risks) |
| Denial of Service | N/A — sin cambio de disponibilidad |
| Elevation of Privilege | N/A |

## Risks

| # | Riesgo | STRIDE | Likelihood | Impact | Mitigación |
|---|--------|--------|------------|--------|------------|
| TM-01 | `backend/.dockerignore`, tal como estaba especificado originalmente en el spec ("espeja `frontend/.dockerignore` completo": `bin/`, `obj/`, `.git/`, `*.env`), pierde el patrón `appsettings.*.local.json` que el `.dockerignore` de raíz actual sí tiene. Ese patrón existe porque `BingoCart.Api` es un proyecto .NET donde un `appsettings.{Environment}.local.json` es el mecanismo estándar de override local con secretos reales (connection string, JWT signing key) — `frontend/.dockerignore` no lo necesita porque Angular no tiene ese mecanismo. "Espejar frontend/.dockerignore" para el backend era un espejo de sintaxis, no de propósito. | Information Disclosure | Low-Medium (requiere que un desarrollador cree ese archivo localmente) | High (secretos embebidos en una capa de imagen Docker, persistentes aunque se borren en una capa posterior, exportables por cualquiera con acceso a la imagen) | Agregar `appsettings.*.local.json` al contenido de `backend/.dockerignore` en Block 2 del spec, además de los 4 patrones ya mirroreados de `frontend/.dockerignore`. |

## Sensitive data classification (F-TM-05)

- **Credentials** — connection strings / JWT signing key en un eventual `appsettings.*.local.json`
  no versionado. No existe hoy en el repo (confirmado con `find`); el control es preventivo, no
  reactivo a un archivo ya presente.

## Encryption (F-TM-07)

N/A — el control aquí es exclusión del build context, no cifrado. Es el mismo enfoque que ya usan
tanto `frontend/.dockerignore` como el `.dockerignore` de raíz actual (excluir, no cifrar, los
secretos locales).

## Mitigations folded into the spec

1. Block 2, archivo `backend/.dockerignore`: contenido final = `bin/`, `obj/`, `.git/`, `*.env`, y
   además `appsettings.*.local.json`. Aplicado en `docs/daw/specs/spec-FEAT-002.md` (sección
   "Decisiones cerradas en PLAN" y Block 2 → Files).

## Result

Risks: C:0 H:1 M:0 L:0 — el único HIGH tiene mitigación folded en el spec antes de escribirlo a
disco. **PASSED.**
