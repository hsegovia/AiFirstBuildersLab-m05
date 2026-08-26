# ADR-005: Getter síncrono `SessionService.rolActual` en vez de `rol$.pipe(take(1))`

| Field | Value |
|-------|-------|
| Date | 2026-08-26 |
| Ticket | FEAT-010a |
| Status | Accepted |

## Context

El spec de Block 4 (`docs/daw/specs/spec-FEAT-010a.md`, líneas 233-235) describía que los guards de
rol consultarían el rol resuelto "tomando el primer valor ya resuelto" de `SessionService.rol$`,
previendo implícitamente algo como `rol$.pipe(take(1), map(...))` dentro del guard — coherente con
que `SessionService` (Block 2) expone únicamente `Observable`s (`rol$`, `mail$`, `sesion$`), sin
ningún acceso síncrono al estado.

Al implementar Block 4, el `daw-implementer` agregó en su lugar un getter síncrono nuevo,
`SessionService.rolActual`, que lee `sesionSubject.getValue()?.rol ?? null` directamente sobre el
`BehaviorSubject` privado ya existente. `daw-arch-auditor`, al auditar el bloque, señaló la
divergencia respecto del texto literal del spec y pidió que quedara una decisión explícita en vez
de una desviación silenciosa — es el primer punto del proyecto donde el estado de sesión se lee de
forma síncrona en lugar de reactiva.

## Options considered

### Opción 1: Getter síncrono `rolActual` (lo implementado)
- **Pros:** el guard queda más simple de leer (`if (sessionService.rolActual === 'Organizador')`
  en vez de un `pipe`/`map` para extraer un valor que, dado el `APP_INITIALIZER` de Block 2, siempre
  está disponible sincrónicamente en el momento en que el router evalúa un guard — el `take(1)`
  nunca esperaría una segunda emisión en la práctica, así que envolverlo en un Observable solo para
  desenvolverlo inmediatamente después es indirección sin beneficio real.
- **Cons:** introduce una segunda forma de leer el mismo estado (síncrona, junto a las 3 ya
  existentes basadas en `Observable`), rompiendo la homogeneidad "todo reactivo" que Block 2 había
  establecido como único acceso a la sesión.

### Opción 2: `rol$.pipe(take(1), map(rol => rol === 'Organizador'))` dentro del guard
- **Pros:** mantiene `SessionService` 100% reactivo, sin una superficie de acceso nueva; es lo que
  el spec preveía literalmente.
- **Cons:** el `CanActivateFn` de Angular Router acepta devolver un `Observable<boolean | UrlTree>`,
  así que es funcionalmente viable, pero añade una capa de operadores RxJS para extraer un valor que
  ya está disponible en memoria — indirección que no compra ninguna garantía adicional, dado que la
  garantía real (que la sesión ya está resuelta) la da `APP_INITIALIZER`, no el `take(1)`.

## Decision

Se adopta la **Opción 1**, el getter síncrono `rolActual`, confirmada tras la revisión de
`daw-arch-auditor` y `daw-module-verifier` (ambos PASSED sin objeción de fondo, solo la observación
de que la decisión quedara registrada). El comportamiento resultante es idéntico al que la Opción 2
habría producido — la diferencia es puramente de estilo interno del servicio, no de contrato
observable desde afuera.

## Consequences

- `SessionService` expone ahora 4 formas de leer el estado de sesión: 3 reactivas (`rol$`, `mail$`,
  `sesion$`, sin cambios respecto de Block 2) más 1 síncrona (`rolActual`), documentada con un
  comentario que explica por qué existe (guards que necesitan una decisión síncrona en el momento en
  que el router los evalúa).
- Cualquier código futuro que necesite leer el rol de forma síncrona (fuera de un guard) puede
  reusar `rolActual` en vez de inventar un tercer mecanismo — Block 5 y Block 6, y los sub-tickets
  FEAT-010b/c/d/e que dependen de este bloque, tienen esta opción disponible.
- No se requiere ningún cambio de código: esta ADR documenta una decisión ya tomada e implementada,
  confirmada como aceptable en la revisión de Block 4.
