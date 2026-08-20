# PRD FEAT-008b: Carrito de compras

| Field | Value |
|-------|-------|
| Ticket | FEAT-008b |
| Tracker | none |
| Date | 2026-08-20 |
| PRD loops | 0 |

## Context and Problem

FEAT-008a resolvió la mitad de solo lectura del flujo del participante: descubrir cartones (RF-07,
RF-08), ya mergeado a `main`. Pero un visitante todavía no tiene forma de retener ninguno de esos
cartones entre una tanda y la siguiente — cada solicitud de descubrimiento es independiente, sin
estado.

Este ticket es la segunda mitad del split (RF-09 a RF-13c del PRD maestro,
`docs/daw/prd/prd-bingocartV2.md`, mismo patrón que FEAT-001a/b y FEAT-008a/b): un carrito de
compras por participante **sin requerir registro ni login** (eso llega recién en RF-14, fuera de
alcance), identificado por una sesión anónima (cookie/token), con reserva temporal de 5 minutos que
evita que dos participantes compren el mismo cartón mientras uno lo tiene en su carrito. Introduce
la primera pieza de estado por sesión anónima del proyecto y la primera dependencia real de Redis
(ya declarado en el stack, sin uso hasta ahora).

## Goals

- Un participante sin registrarse puede agregar hasta 5 cartones de una tanda presentada a un
  carrito propio (RF-09).
- El carrito persiste entre tandas sucesivas dentro del mismo navegador, mediante una identificación
  de sesión anónima (RF-09b).
- Un participante puede descartar la tanda actual y pedir una nueva sin que se le repitan cartones ya
  agregados o ya descartados (RF-10).
- El carrito acumula cartones de tandas sucesivas con visibilidad de cantidad y monto total (RF-11).
- Un participante puede quitar cartones individuales de su carrito antes de comprar (RF-12).
- Ningún cartón queda reservado indefinidamente: el carrito completo se libera a los 5 minutos del
  último agregado si no se confirma la compra (RF-13a, RF-13b, RF-13c).

## Functional Requirements

- FR-01: El sistema debe permitir agregar de 0 a 5 cartones de la tanda presentada al carrito del
  participante, sin requerir registro ni inicio de sesión.
- FR-02: El sistema debe identificar al participante no registrado mediante un identificador de
  sesión (cookie o token) persistente en el navegador, para sostener el carrito y el historial de
  cartones descartados entre tandas sucesivas.
- FR-03: El sistema debe permitir descartar los cartones no seleccionados de la tanda actual y
  solicitar una nueva tanda de 5 cartones, sin repetir cartones ya agregados al carrito ni cartones
  ya descartados en tandas anteriores de la misma sesión, mientras haya stock disponible, sin límite
  de reintentos.
- FR-04: El sistema debe mantener un carrito por sesión que acumule todos los cartones seleccionados
  en tandas sucesivas, exponiendo el total de cartones acumulados y el monto total.
- FR-05: El sistema debe permitir eliminar cartones individuales del carrito antes de confirmar la
  compra.
- FR-06: El sistema debe reservar el carrito completo por 5 minutos desde el último cartón agregado,
  impidiendo que otro participante agregue a su propio carrito un cartón ya reservado.
- FR-07: El sistema debe reiniciar el plazo de reserva de 5 minutos para todos los cartones del
  carrito cada vez que se agrega un nuevo cartón.
- FR-08: El sistema debe liberar todos los cartones del carrito, devolviéndolos a disponibles, si la
  compra no se confirma dentro del plazo de reserva vigente.
- FR-09: El sistema debe recalcular la cantidad de cartones y el monto total del carrito cada vez que
  se agrega o se quita un cartón.
- FR-10: El sistema debe rechazar el agregado de un cartón al carrito si ese cartón ya está reservado
  por otra sesión o ya fue vendido.

## Non-Functional Requirements

- NFR-01: La reserva y liberación de cartones debe ser atómica: dos sesiones distintas no pueden
  reservar el mismo cartón simultáneamente (RNF-03 del PRD maestro), verificado con al menos una
  prueba de reserva concurrente sobre el mismo cartón.
- NFR-02: El sistema debe limitar a 60 solicitudes de operaciones de carrito (agregar/quitar/pedir
  tanda nueva, combinadas) por IP cada 5 minutos, mismo criterio de mitigación de abuso que
  FEAT-008a.
- NFR-03: Quitar un cartón del carrito no debe modificar el instante de vencimiento de la reserva de
  los cartones restantes (solo agregar un cartón lo reinicia, ver FR-07).

## Acceptance Criteria

- AC-01 (FR-01): WHEN un participante sin autenticar selecciona hasta 5 cartones de la tanda
  presentada, THE sistema SHALL agregarlos a su carrito sin exigir registro ni login.
- AC-02 (FR-02): WHEN un participante sin autenticar interactúa por primera vez con el carrito, THE
  sistema SHALL asignarle un identificador de sesión persistente en el navegador para sostener el
  carrito entre solicitudes sucesivas.
- AC-03 (FR-03): WHEN un participante descarta la tanda actual y pide una nueva, THE sistema SHALL
  devolver 5 cartones distintos, sin repetir los ya agregados al carrito ni los ya descartados en
  tandas anteriores de esa misma sesión.
- AC-04 (FR-04): WHEN un participante agregó cartones en más de una tanda, THE sistema SHALL mostrar
  el carrito con todos los cartones acumulados, su cantidad total y el monto total.
- AC-05 (FR-05): WHEN un participante elimina un cartón de su carrito, THE sistema SHALL quitarlo del
  carrito, recalcular cantidad y monto (FR-09), y devolverlo a disponible para otras sesiones.
- AC-06 (FR-06, FR-07): WHEN un participante agrega un cartón a su carrito, THE sistema SHALL
  reservarlo por 5 minutos y reiniciar el plazo de reserva de todo el carrito a 5 minutos desde ese
  agregado.
- AC-07 (FR-08): IF pasan 5 minutos desde el último agregado sin que la compra se confirme, THEN THE
  sistema SHALL liberar todos los cartones del carrito, quedando disponibles para otras sesiones.
- AC-08 (NFR-03): WHEN un participante elimina un cartón de un carrito con otros cartones cuya
  reserva vence en un instante determinado, THE sistema SHALL mantener ese mismo instante de
  vencimiento para los cartones restantes, sin reiniciarlo a 5 minutos.
- AC-09 (FR-10): IF un cartón solicitado ya está reservado por otra sesión o ya fue vendido, THEN THE
  sistema SHALL rechazar el agregado al carrito e informar que ya no está disponible.
- AC-10 (FR-02): IF un participante pierde su identificador de sesión (cookies eliminadas o cambio de
  dispositivo), THEN THE sistema SHALL no poder recuperar su carrito ni su historial de descartes
  previos — debe comenzar de nuevo.

## Out of Scope

- Confirmación de compra, registro/login del comprador y todo lo posterior (RF-14 en adelante) —
  tickets futuros.
- Bloqueo de confirmación de compra con carrito vacío (RF-28) — pertenece al flujo de confirmación,
  no al carrito en sí.
- Descubrimiento de cartones (RF-07, RF-08) — ya resuelto en FEAT-008a.
- Cualquier pantalla de carrito en el frontend — backend-only, mismo criterio que
  FEAT-003/FEAT-004/FEAT-005/FEAT-007/FEAT-008a.
- Persistencia del carrito más allá de la sesión anónima (ej. recuperarlo en otro dispositivo) — el
  PRD maestro define explícitamente que se pierde (RF-09b).

## Risks and Mitigations

- **Riesgo:** dos sesiones podrían reservar el mismo cartón si la operación de reserva no es
  atómica, vendiendo el mismo cartón dos veces. **Mitigación:** NFR-01 exige atomicidad verificada
  con una prueba de concurrencia; el mecanismo concreto (lock optimista en SQL Server, operación
  atómica de Redis, o ambos) se decide en PLAN.
- **Riesgo:** una reserva que nunca expira agotaría el stock de cartones disponibles ante
  participantes que abandonan el carrito. **Mitigación:** RF-08/AC-07 exige liberación automática a
  los 5 minutos; se implementa con TTL de Redis, sin proceso de limpieza manual.
- **Riesgo:** una sesión anónima sin autenticación es más difícil de asociar a un abuso reiterado
  (crear y abandonar carritos repetidamente para agotar stock ajeno). **Mitigación:** NFR-02 (rate
  limiting por IP) mismo patrón que FEAT-008a; no se introduce ninguna mitigación adicional en este
  ticket.

## Dependencies

- `Bingo`/`Carton` (Domain, FEAT-003).
- Descubrimiento de cartones (FEAT-008a, ya en `main`) — origen de los cartones que se agregan al
  carrito.
- Redis, declarado en el stack (`AGENTS.md`) pero sin uso hasta este ticket — primera vez que se
  incorpora al backend.
