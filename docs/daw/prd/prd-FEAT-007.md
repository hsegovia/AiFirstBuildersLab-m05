# PRD FEAT-007: Editar y eliminar bingo sin compras

| Field | Value |
|-------|-------|
| Ticket | FEAT-007 |
| Tracker | none |
| Date | 2026-08-19 |
| PRD loops | 0 |

## Context and Problem

El organizador puede crear un bingo (FEAT-003) y listar los suyos (FEAT-004), pero una vez creado no
tiene forma de corregir un dato mal cargado (nombre del evento, fecha de sorteo, costo por cartón) ni
de eliminar un bingo que ya no va a usar. Hoy la única forma de "corregir" un error es crear otro
bingo — y como un organizador solo puede tener un bingo con sorteo vigente a la vez (regla de
FEAT-003), un error de carga bloquea al organizador hasta que pase la fecha de sorteo del bingo mal
cargado.

Este ticket cierra ese hueco para el caso simple: un bingo que todavía no tiene ninguna compra
asociada. RF-25/RF-26/RF-27 del PRD maestro (`docs/daw/prd/prd-bingocartV2.md`) hablan de "compra
registrada (pendiente o confirmada)" como la condición que bloquea la edición/eliminación — pero el
concepto de compra (RF-07 a RF-21 del roadmap) todavía no existe en el dominio. Por eso este ticket
implementa el chequeo de forma honesta con el estado actual: **hoy, todo bingo cumple "sin compras
registradas"**, así que edición y eliminación funcionan siempre. El chequeo queda expresado de forma
explícita (no como un `true` hardcodeado) para que, cuando el ticket de carrito/compra agregue la
entidad `Compra`, alcance con completar esa consulta sin tocar la lógica de autorización que ya
existe acá.

## Goals

- El organizador puede corregir nombre de evento, fecha de sorteo y costo por cartón de un bingo
  propio, sin necesidad de eliminarlo y recrearlo.
- El organizador puede eliminar un bingo propio que ya no quiere mantener publicado.
- Ningún organizador puede editar ni eliminar un bingo que no le pertenece.
- El chequeo "sin compras registradas" queda modelado explícitamente, listo para activarse cuando
  exista la entidad `Compra`, sin requerir un ticket de refactor futuro.

## Functional Requirements

- FR-01: El sistema debe permitir a un organizador autenticado editar el nombre del evento, la fecha
  y hora del sorteo y el costo por cartón de un bingo propio.
- FR-02: El sistema debe rechazar la edición de un bingo que no exista o que no pertenezca al
  organizador autenticado, sin distinguir entre ambos casos en la respuesta (mismo criterio de
  no-enumeración que el resto del proyecto).
- FR-03: El sistema debe revalidar, al editar la fecha de sorteo, que la nueva fecha sea posterior al
  momento de la edición (misma regla que la creación, FEAT-003).
- FR-04: El sistema debe rechazar la edición de un bingo que tenga al menos una compra registrada
  (pendiente o confirmada). Hoy esta condición nunca se cumple porque el dominio no tiene el
  concepto de compra — el chequeo queda implementado explícitamente para activarse sin cambios
  cuando esa entidad exista.
- FR-05: El sistema debe permitir a un organizador autenticado eliminar un bingo propio.
- FR-06: El sistema debe rechazar la eliminación de un bingo que no exista, que no pertenezca al
  organizador autenticado, o que tenga al menos una compra registrada (mismo criterio que FR-02 y
  FR-04).
- FR-07: El sistema debe eliminar, junto con el bingo, todos sus cartones asociados.

## Non-Functional Requirements

- NFR-01: El `organizadorId` usado para validar la pertenencia del bingo debe derivarse
  exclusivamente del JWT de la sesión autenticada, nunca de un parámetro de la request (mismo
  criterio que `POST /api/bingos` y `GET /api/bingos`, mitigación de IDOR).
- NFR-02: La operación de eliminación (bingo + cartones) debe ser atómica: o se eliminan ambos, o no
  se elimina ninguno.

## Acceptance Criteria

- AC-01 (FR-01, NFR-01): WHEN un organizador autenticado envía una edición válida (nombre de evento,
  fecha de sorteo futura, costo por cartón) para un bingo propio sin compras, THE sistema SHALL
  actualizar el bingo y devolver sus datos actualizados.
- AC-02 (FR-02): IF el bingo indicado no existe o pertenece a otro organizador, THEN THE sistema
  SHALL rechazar la edición con 404, sin revelar si el bingo existe bajo otro dueño.
- AC-03 (FR-03): IF la nueva fecha de sorteo no es posterior al momento de la edición, THEN THE
  sistema SHALL rechazar la edición con 400.
- AC-04 (FR-04): IF el bingo tiene al menos una compra registrada, THEN THE sistema SHALL rechazar la
  edición con 409.
- AC-05 (FR-05, FR-07, NFR-02): WHEN un organizador autenticado elimina un bingo propio sin compras,
  THE sistema SHALL eliminar el bingo y todos sus cartones, y devolver 204.
- AC-06 (FR-06): IF el bingo indicado no existe o pertenece a otro organizador, THEN THE sistema
  SHALL rechazar la eliminación con 404.
- AC-07 (FR-06): IF el bingo tiene al menos una compra registrada, THEN THE sistema SHALL rechazar la
  eliminación con 409.
- AC-08 (FR-01, FR-05): WHEN un visitante sin autenticar intenta editar o eliminar cualquier bingo,
  THE sistema SHALL rechazar la operación con 401.

## Out of Scope

- La generación de nuevos cartones al editar `CantidadCartones` — ese campo no es editable en este
  ticket (cambiarlo implicaría regenerar cartones ya expuestos, fuera de alcance).
- El chequeo real contra compras — no existe la entidad `Compra` todavía; el chequeo queda
  implementado sobre datos que hoy siempre están vacíos, listo para conectarse a la entidad real
  cuando el ticket de carrito/compra la introduzca.
- Cualquier pantalla de edición/eliminación en el frontend — backend-only, mismo criterio que
  FEAT-003/FEAT-004/FEAT-005.
- Notificación por mail al eliminar un bingo — no hay comprador todavía a quien notificar.

## Risks and Mitigations

- **Riesgo:** el chequeo "sin compras registradas" queda como una consulta que hoy siempre devuelve
  vacío, lo que podría leerse como un TODO disfrazado. **Mitigación:** se modela como una consulta
  explícita y testeada (no un `true`/`false` hardcodeado), documentada en el spec como el punto de
  extensión para cuando exista `Compra` — el PLAN debe dejar visible dónde y cómo se conecta.
- **Riesgo:** eliminar cartones junto con el bingo podría dejar huérfanos si la operación no es
  atómica. **Mitigación:** NFR-02 exige atomicidad; el spec debe especificar el mecanismo (
  transacción o `OnDelete(Cascade)` de EF Core) y un test que lo verifique.

## Dependencies

- `Bingo` y `Carton` (Domain, FEAT-003).
- `IBingoRepository`/`BingoRepository` (FEAT-003/FEAT-004) — este ticket probablemente agrega
  métodos nuevos (`ObtenerPorIdAsync`, `ActualizarAsync`, `EliminarAsync`), no un repositorio nuevo.
- Autenticación JWT (FEAT-001b) — mismo patrón de `[Authorize]` + claim `NameIdentifier` que
  `BingosController`.
