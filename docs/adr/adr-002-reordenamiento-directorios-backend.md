# ADR-002: Reordenamiento de directorios — `src/`/`tests/` bajo `backend/`

| Field | Value |
|-------|-------|
| Date | 2026-08-16 |
| Ticket | N/A (surgió en revisión humana del cierre de RELEASE de FEAT-001a); ejecutado en FEAT-002 |
| Status | Accepted, Amended 2026-08-16 (ver Amendment) |

## Context

Al cerrar FEAT-001a, la revisión humana del PR notó que el esquema de directorios de nivel raíz es
asimétrico: `frontend/` tiene un nombre claro y agrupa todo su código (fuente + tests), pero el
backend vive directo en `src/` (nombre genérico) con `tests/` como hermano al mismo nivel — no hay
una carpeta `backend/` equivalente a `frontend/` que agrupe ambos.

Corregirlo implica mover `src/` → `backend/src/` y `tests/` → `backend/tests/`, con impacto en
`BingoCart.sln` (rutas de los `.csproj`), `docker-compose.yml` (contexto de build del servicio
`api`), `src/BingoCart.Api/Dockerfile` (rutas de `COPY`), `.dockerignore`, y potencialmente
cualquier pipeline de CI que asuma las rutas actuales.

## Options considered

### Opción 1: Reordenar ahora, antes del merge de FEAT-001a
- **Pros:** corrige la asimetría antes de que el esquema se vuelva la convención establecida y más
  código se acumule sobre ella.
- **Cons:** cambio no funcional que toca build/deploy en un PR ya verificado y aprobado —
  requeriría re-verificar build + tests + `docker-compose up --build` completo antes de cerrar,
  agregando riesgo y tiempo a un ticket que ya había cerrado su alcance funcional.

### Opción 2: Documentar como decisión diferida, con ticket de seguimiento
- **Pros:** no bloquea ni reabre un ticket ya cerrado y verificado; el reordenamiento es
  estructural, no funcional, y puede hacerse de forma aislada en su propio ticket con su propia
  verificación completa.
- **Cons:** el esquema asimétrico queda en el repo (y en `main`, tras el merge de FEAT-001a) hasta
  que se ejecute el ticket de seguimiento.

## Decision

Se adopta la **Opción 2**, por decisión explícita del usuario.

## Consequences

- El esquema actual (`src/`, `tests/`, `frontend/` como hermanos en la raíz) se mantiene hasta que
  se ejecute el ticket de seguimiento.
- **Pendiente:** crear un ticket (FIX o FEATURE según el alcance final que se decida) para:
  1. Mover `src/*` → `backend/src/*` y `tests/*` → `backend/tests/*`.
  2. Actualizar `BingoCart.sln` (rutas de proyecto).
  3. Actualizar `docker-compose.yml` (contexto de build de `api`, hoy `context: .`).
  4. Actualizar `src/BingoCart.Api/Dockerfile` (rutas de `COPY` sobre `src/`).
  5. Actualizar `.dockerignore` (patrones que hoy asumen `src/`/`frontend/` en la raíz).
  6. Re-verificar: `dotnet build`, suite completa de tests, `docker-compose up --build` end-to-end.
- Cualquier ticket nuevo que agregue proyectos .NET (ej. FEAT-001b) hereda el esquema actual
  (`src/BingoCart.<Proyecto>`) hasta que este ADR se ejecute — no se debe anticipar el
  reordenamiento parcialmente en un ticket no relacionado.

## Amendment (2026-08-16, durante PLAN de FEAT-002)

Al diseñar el ticket de seguimiento (FEAT-002), el PRD propuso `backend/BingoCart.Api/` **sin** el
subnivel `src/` intermedio que este ADR había decidido originalmente (`backend/src/BingoCart.Api/`).
`daw-arch-auditor` detectó la divergencia entre el PRD y esta decisión ya `Accepted`, y la escaló
antes de escribir el spec — correctamente, un ADR aceptado no debe quedar sobreescrito en silencio.

**Nueva decisión, reemplazando el punto 1 del checklist original:** los proyectos van directo en
`backend/BingoCart.Api/`, `backend/BingoCart.Domain/`, `backend/BingoCart.Application/`,
`backend/BingoCart.Infrastructure/` (sin `backend/src/`), y los tests en `backend/tests/`.

**Justificación:** `daw-arch-auditor` confirmó que `AGENTS.md` (sección "Architecture conventions")
no exige ningún subnivel `src/` para el backend — solo nombra las carpetas de capa (`Api/`,
`Application/`, `Domain/`, `Infrastructure/`). La comparación con `frontend/` que motivó este ADR es
válida a nivel de directorio raíz (`backend/` y `frontend/` como únicos directorios de código de
nivel raíz, cada uno autocontenido) — `frontend/` en sí ya es el directorio autocontenido, sin que
haga falta que sus proyectos internos repliquen un subnivel `src/` para que la analogía se sostenga.
Agregar `src/` dentro de `backend/` habría sido una capa de anidamiento sin equivalente real en
`frontend/` ni beneficio claro.

**Quién la aceptó:** el usuario, durante la fase PLAN de FEAT-002, tras la escalación del auditor.

**Condición de revisión:** ninguna prevista — es la estructura final, no un riesgo a reevaluar.
