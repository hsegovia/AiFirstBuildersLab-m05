# PRD FEAT-010c: Comprador — descubrimiento y carrito

| Field | Value |
|-------|-------|
| Ticket | FEAT-010c |
| Tracker | none |
| Date | 2026-08-25 |
| PRD loops | 0 |

## Context and Problem

Split 3 de 5 de FEAT-010 (`docs/daw/prd/prd-FEAT-010.md`). El backend de directorio público
(FEAT-005), descubrimiento de cartones (FEAT-008a) y carrito con reserva atómica en Redis
(FEAT-008b) está completo, pero sin pantalla. Este sub-ticket construye el recorrido anónimo
completo: elegir un organizador (o descubrir al azar), seleccionar cartones y armar el carrito —
todo sin requerir autenticación (RF-14 del PRD maestro exige que el registro/login recién se pida
al confirmar la compra, que es FEAT-010d).

No depende de FEAT-010b ni de autenticación de comprador — los 7 endpoints que consume
(`CartonesController`, `CarritoController`) son `[AllowAnonymous]`. Sí depende de FEAT-010a para el
interceptor de errores y el layout base.

## Goals

- Que un visitante sin cuenta pueda ver organizadores con evento activo, descubrir cartones y
  armar un carrito de punta a punta.
- Que el carrito persista entre tandas de descubrimiento sucesivas, vía la sesión anónima que el
  backend ya sostiene con su propia cookie.
- Que ningún cartón ya agregado o ya descartado se repita en una tanda nueva.

## Functional Requirements

- FR-01: El sistema debe presentar un directorio público, paginado, de organizadores con evento
  activo, accesible sin autenticación.
- FR-02: El sistema debe presentar al participante 5 cartones aleatorios de cualquier organizador
  (descubrimiento global), sin requerir autenticación.
- FR-03: El sistema debe permitir al participante seleccionar un organizador del directorio y ver 5
  cartones de ese organizador (descubrimiento por organizador), sin requerir autenticación.
- FR-04: El sistema debe permitir al participante agregar cartones presentados a su carrito y
  quitarlos, sin requerir autenticación.
- FR-05: El sistema debe permitir al participante descartar la tanda actual y pedir una tanda nueva
  de 5 cartones, sin repetir cartones ya agregados al carrito ni ya descartados.
- FR-06: El sistema debe mostrar el carrito acumulado del participante, con el detalle de cada
  cartón, la cantidad total y el monto total.
- FR-07: El sistema debe permitir al participante eliminar un cartón individual de su carrito antes
  de confirmar la compra.

## Non-Functional Requirements

- NFR-01: Las pantallas son usables en viewport móvil (≥360px de ancho).
- NFR-02: El carrito anónimo persiste entre las llamadas de FR-04/FR-05/FR-06/FR-07 exclusivamente
  vía la cookie de sesión que el backend ya emite (`bingocart_carrito`) — el frontend no guarda el
  estado del carrito en `localStorage` ni lo reconstruye del lado del cliente.
- NFR-03: Cada llamada a los 7 endpoints de este sub-ticket viaja con `withCredentials`, para que la
  cookie de carrito se envíe y se reciba correctamente.

## Acceptance Criteria

*(EARS — ver `.daw/rules/validation-rules.instructions.md` §1)*

- AC-01: WHEN un visitante anónimo abre el directorio público, THE sistema SHALL mostrar los
  organizadores con evento activo, paginados, sin pedir autenticación (FR-01).
- AC-02: WHEN un visitante anónimo abre el descubrimiento global, THE sistema SHALL mostrar hasta 5
  cartones aleatorios de cualquier organizador con stock disponible (FR-02).
- AC-03: WHEN un visitante anónimo selecciona un organizador del directorio, THE sistema SHALL
  mostrar hasta 5 cartones de ese organizador (FR-03).
- AC-04: WHEN un visitante anónimo agrega un cartón presentado a su carrito, THE sistema SHALL
  reflejar el cartón en el carrito y permitir seguir descubriendo sin perder la selección previa
  (FR-04).
- AC-05: IF un cartón ya fue reservado por otra sesión al intentar agregarlo (409), THEN THE
  sistema SHALL informar que el cartón ya no está disponible y quitarlo de la tanda mostrada
  (FR-04).
- AC-06: WHEN un visitante anónimo pide una nueva tanda de descubrimiento, THE sistema SHALL
  mostrar hasta 5 cartones nuevos que no estén ya en su carrito ni entre los ya descartados en esa
  sesión (FR-05).
- AC-07: WHEN un visitante abre su carrito, THE sistema SHALL mostrar cada cartón agregado, la
  cantidad total y el monto total (FR-06).
- AC-08: WHEN un visitante elimina un cartón individual de su carrito, THE sistema SHALL quitarlo
  del carrito mostrado sin afectar a los demás cartones ya agregados (FR-07).

## Out of Scope

- **Confirmar la compra del carrito** — exige autenticación de comprador, es FEAT-010d.
- **Gestión de bingos del organizador** — FEAT-010b.
- **Mis cartones / cuenta del comprador** — FEAT-010e.
- **Cualquier persistencia del carrito más allá de la sesión anónima del navegador** — si el
  visitante cambia de dispositivo o borra las cookies, pierde el carrito (comportamiento ya
  establecido por el backend en FEAT-008b, no se cambia acá).

## Risks and Mitigations

- **R-01 — Reserva expirada mientras el visitante navega.** El carrito tiene una reserva de 5
  minutos en el backend (FEAT-008b); si expira mientras el visitante sigue en la pantalla, la
  siguiente acción sobre ese cartón devolverá un error que el interceptor de FEAT-010a debe poder
  mostrar sin romper la pantalla — la responsabilidad de este sub-ticket es solo reflejar ese error,
  no inventar un mecanismo de renovación.
- **R-02 — Concurrencia entre visitantes por el mismo cartón** (AC-05): el 409 de "cartón ya
  reservado" es esperable en tráfico real, no un caso raro — la UI debe tratarlo como flujo normal,
  no como error inesperado.

## Dependencies

- **FEAT-010a** (infraestructura transversal) — interceptor HTTP, layout base. Debe estar mergeado
  antes de empezar.
- Backend: `CartonesController`, `CarritoController` — ya existen en `main` desde FEAT-005,
  FEAT-008a y FEAT-008b, sin cambios.
