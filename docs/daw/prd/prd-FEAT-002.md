# PRD FEAT-002: Reordenar directorios backend bajo backend/ (ADR-002)

| Field | Value |
|-------|-------|
| Ticket | FEAT-002 |
| Tracker | none |
| Date | 2026-08-16 |
| PRD loops | 1 |

## Context and Problem

`docs/adr/adr-002-reordenamiento-directorios-backend.md` documenta una asimetría en el esquema de
directorios de nivel raíz: `frontend/` agrupa todo su código (fuente + tests) bajo un nombre claro,
pero el backend vive directo en `src/` (nombre genérico) con `tests/` como hermano al mismo nivel —
sin una carpeta `backend/` equivalente. El ADR difirió la corrección a este ticket de seguimiento.

Este ticket ejecuta la decisión ya tomada en el ADR: mover el código backend a `backend/`, sin
cambiar ninguna lógica de negocio ni comportamiento — es un movimiento de archivos y ajuste de
referencias, puro.

## Goals

| # | Objetivo | Métrica de éxito |
|---|----------|-------------------|
| G1 | Simetría de esquema de directorios entre backend y frontend | `backend/` y `frontend/` como únicos directorios de código de nivel raíz, ambos autocontenidos |
| G2 | Cero regresión funcional | 100% de los tests existentes (43 .NET) siguen pasando, `docker-compose up --build` sigue funcionando end-to-end |

## Functional Requirements

- FR-01: El sistema debe reubicar los 4 proyectos de producción (`BingoCart.Api`,
  `BingoCart.Application`, `BingoCart.Domain`, `BingoCart.Infrastructure`) de `src/` a `backend/`,
  sin subnivel `src/` intermedio (ej. `backend/BingoCart.Api/`).
- FR-02: El sistema debe reubicar los 5 proyectos de test (`BingoCart.Domain.Tests`,
  `BingoCart.Application.Tests`, `BingoCart.Infrastructure.Tests`, `BingoCart.Api.Tests`,
  `BingoCart.E2E.Tests`) de `tests/` a `backend/tests/`.
- FR-03: El sistema debe mover `BingoCart.sln` de la raíz del repo a `backend/BingoCart.sln`,
  actualizando todas las rutas de proyecto (`Project(...)`) y `ProjectReference` a las nuevas
  ubicaciones relativas.
- FR-04: El sistema debe actualizar `docker-compose.yml` para que el servicio `api` use
  `context: ./backend` en vez de `context: .` (espejo exacto de `context: ./frontend` que ya usa el
  servicio `web`).
- FR-05: El sistema debe actualizar el `Dockerfile` de la Api (reubicado a
  `backend/BingoCart.Api/Dockerfile`) para reflejar las rutas relativas dentro del nuevo contexto
  `backend/` (ya no necesita ver nada fuera de `backend/`).
- FR-06: El sistema debe crear `backend/.dockerignore` (espejo de `frontend/.dockerignore`) y
  eliminar del `.dockerignore` de raíz cualquier patrón que ya no aplique tras el cambio de
  contexto del servicio `api`.
- FR-07: El sistema debe actualizar `.gitignore` si contiene patrones específicos de `src/`/`tests/`
  que deban re-apuntar a `backend/src/`... — es decir, a las nuevas rutas bajo `backend/`.

## Non-Functional Requirements

- NFR-01: El reordenamiento no debe introducir ninguna regresión funcional: 0 de 43 tests .NET
  existentes rotos, medido corriendo la suite completa antes y después del cambio.
- NFR-02: `docker-compose up --build` debe completar sin errores tras el cambio, verificado con un
  ciclo completo `docker-compose down -v && docker-compose up --build` desde cero (simulando un
  clone limpio), no solo con los contenedores ya existentes.

## Acceptance Criteria

- AC-01 (FR-01, FR-02, FR-03): WHEN se ejecuta `dotnet build backend/BingoCart.sln`, THE sistema
  SHALL compilar los 9 proyectos (4 de producción + 5 de test) sin errores.
- AC-02 (FR-01, FR-02, FR-03): WHEN se ejecuta `dotnet test backend/BingoCart.sln`, THE sistema
  SHALL ejecutar los mismos 43 tests .NET que existían antes del reordenamiento, todos en verde.
- AC-03 (FR-04, FR-05, FR-06): WHEN se ejecuta `docker-compose down -v && docker-compose up
  --build` desde cero, THE sistema SHALL levantar los 3 servicios (`db`, `api`, `web`) sin errores.
- AC-04 (FR-04, FR-05): WHEN se hace `POST /api/organizadores/registro` contra el stack
  contenedorizado post-reordenamiento, THE sistema SHALL responder 201 exactamente igual que antes
  del cambio (regresión funcional cero en el flujo end-to-end).
- AC-05 (FR-01, FR-02, FR-03, FR-04, FR-05, FR-06, FR-07): IF algún archivo del repo (código,
  configuración, CI) referencia una ruta antigua (`src/BingoCart.*`, `tests/BingoCart.*`, o
  `BingoCart.sln` en la raíz) tras completar el reordenamiento, THEN THE sistema SHALL fallar el
  build o la verificación correspondiente, haciendo visible cualquier referencia rota en vez de
  fallar silenciosamente en runtime.

## Out of Scope

- No se toca `frontend/` — ya está correctamente nombrado y estructurado, fuera del alcance de este
  ADR.
- No se renombra ni reestructura ningún proyecto individual (`BingoCart.Api` sigue llamándose
  igual, solo cambia su ubicación).
- No se cambia ninguna lógica de negocio ni comportamiento funcional de la aplicación — es
  exclusivamente un movimiento de archivos y ajuste de referencias.
- No se actualiza ningún pipeline de CI externo (no existe ninguno declarado en el repo hoy,
  confirmado por `/daw-context-check` en CLASSIFY).
- No se implementa FEAT-001b (login de organizador) en este ticket — ese ticket debe branchear
  DESPUÉS de que este se mergee, para heredar la estructura nueva.

## Risks and Mitigations

| # | Riesgo | Impacto | Mitigación |
|---|--------|---------|------------|
| R-01 | Una referencia rota (ruta relativa incorrecta en un `.csproj`, en `docker-compose.yml` o en el `Dockerfile`) pasa desapercibida si no se verifica exhaustivamente. | Alto — el build o el contenedor fallarían en el próximo uso, potencialmente después de mergeado. | AC-01 a AC-04 exigen verificación completa (build, suite de tests, `docker-compose up --build` desde cero, flujo end-to-end real) antes de cerrar el ticket — no alcanza con que compile, tiene que levantar y responder. |
| R-02 | El corrimiento de archivos con `git mv` podría perder el historial de un archivo si se hace como delete+create en vez de rename. | Bajo — no afecta funcionalidad, sí la trazabilidad histórica en `git blame`/`git log --follow`. | Usar `git mv` explícitamente (o mover con `mv` + `git add`, que Git detecta como rename automáticamente por similitud de contenido) en vez de recrear archivos desde cero. |

## Dependencies

Ninguna — este ticket es autocontenido, no depende de otro ticket en curso. Sí es una
**precondición** para cualquier ticket futuro que agregue código backend nuevo (como FEAT-001b,
login de organizador): ese ticket debe branchear desde `main` DESPUÉS de que FEAT-002 se mergee,
para heredar la estructura `backend/` en vez de reintroducir el esquema `src/`/`tests/` antiguo.
