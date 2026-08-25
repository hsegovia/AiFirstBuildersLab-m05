# ADR-004: `APP_INITIALIZER` para resolver la sesión antes de montar la app

| Field | Value |
|-------|-------|
| Date | 2026-08-25 |
| Ticket | FEAT-010a |
| Status | Accepted |

## Context

FEAT-010a (`docs/daw/specs/spec-FEAT-010a.md`, Block 2) necesita que el frontend sepa, antes de
renderizar cualquier ruta, si hay una sesión activa y de qué rol — para que el layout (Block 5) y
los guards (Block 4) no muestren un estado anónimo por un instante y luego "salten" al estado
autenticado tras resolver `GET /api/auth/whoami` (AC-04: un refresh de página con cookie vigente
debe restaurar el layout correcto sin redirigir a login).

El proyecto no tiene, hasta este bloque, ningún precedente de bloquear el montaje de la app detrás
de una llamada de red (`grep APP_INITIALIZER frontend/src` → vacío). Es una decisión que
condiciona el arranque de **toda** la app, no solo de FEAT-010a — los sub-tickets FEAT-010b/c/d/e
heredan este mecanismo sin decidirlo de nuevo.

`daw-arch-auditor`, al auditar el diseño en PLAN, señaló que por ser la primera vez que se introduce
este patrón, y por su alcance transversal, ameritaba quedar documentado como ADR en vez de una
decisión implícita dentro del spec.

## Options considered

### Opción 1: `APP_INITIALIZER` — bloquear el bootstrap hasta resolver `whoami`
- **Pros:** cero parpadeo — el layout se renderiza una sola vez, ya con el rol correcto. Es el
  mecanismo estándar de Angular para "algo que tiene que estar listo antes de que la app exista".
- **Cons:** un fallo de red durante el arranque (5xx, timeout) puede colgar el montaje completo de
  la app si la promesa no se atrapa correctamente — mitigado exigiendo explícitamente en el spec
  (Block 2) que el factory atrape cualquier error y resuelva igual, tratando el fallo como "sesión
  no resuelta".

### Opción 2: Resolver la sesión de forma asíncrona después de montar la app (guard/resolver por ruta)
- **Pros:** no bloquea el render inicial; patrón más granular, cada ruta protegida resuelve la
  sesión solo cuando la necesita.
- **Cons:** el layout (`AppComponent`, siempre visible) mostraría el estado anónimo por un instante
  en cada refresh antes de actualizarse al rol correcto — exactamente el parpadeo que AC-04 quiere
  evitar. Requeriría además resolver la sesión en cada guard por separado (o un `resolve` compartido
  a nivel de ruta raíz), más complejo que un único punto de resolución.

## Decision

Se adopta la **Opción 1**, `APP_INITIALIZER`, con la mitigación explícita en el spec: el factory
atrapa cualquier error de `SessionService.resolverAsync()` y resuelve la promesa de todas formas,
nunca la deja colgada ni rechazada — un fallo de red transitorio nunca debe impedir que la app
monte, solo debe dejarla en estado "sesión no resuelta / anónima" hasta la siguiente llamada
exitosa.

## Consequences

- `frontend/src/app/app.module.ts` registra un provider `APP_INITIALIZER` que invoca
  `SessionService.resolverAsync()` (con el catch obligatorio) antes de que Angular termine el
  bootstrap.
- FEAT-010b/c/d/e heredan este mecanismo sin volver a decidirlo — cualquier guard o componente que
  dependa del rol resuelto puede asumir que `SessionService` ya tiene el estado (o el estado
  explícito "anónimo por fallo") en el momento en que el router evalúa la primera navegación.
- Si en el futuro el costo de este round-trip bloqueante se vuelve un problema de performance
  percibido (ej. mail lento del backend, alta latencia), la alternativa documentada es la Opción 2 —
  no hace falta un ADR nuevo para reconsiderarlo, esta misma decisión ya deja registradas las dos
  opciones y por qué se descartó la segunda.
