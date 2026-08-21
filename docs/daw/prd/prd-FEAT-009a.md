# PRD FEAT-009a: Confirmar compra (núcleo)

| Field | Value |
|-------|-------|
| Ticket | FEAT-009a |
| Tracker | none |
| Date | 2026-08-20 |
| PRD loops | 0 |

## Context and Problem

Hasta FEAT-008b un participante puede armar un carrito de cartones, pero no existe ninguna forma de
convertirlo en una venta real: `Compra` no existe todavía en el dominio (por eso FEAT-006 se pospuso).
Este ticket es el primer paso del lado de "confirmar" (RF-14 a RF-21b del PRD maestro,
`docs/daw/prd/prd-bingocartV2.md`) — se partió en cuatro sub-tickets (FEAT-009a/b/c/d, mismo patrón
que FEAT-001a/b y FEAT-008a/b) porque el bloque completo mezcla registro del comprador, persistencia
de la compra, notificaciones por mail con PDF adjunto (primer uso de MailKit/QuestPDF del proyecto),
gestión manual de pago por el organizador, y la cuenta del comprador — cuatro responsabilidades
independientes entre sí una vez que `Compra` existe.

Este sub-ticket (FEAT-009a) es el núcleo: registrar al comprador, agrupar el carrito por
organizador en compras independientes, y marcar los cartones vendidos de forma atómica. Sin esto,
ningún otro sub-ticket tiene nada sobre lo cual trabajar — FEAT-009b necesita que `Compra` exista
para poder mandar el mail, FEAT-009c necesita compras en estado "pendiente" para poder
confirmarlas/cancelarlas, y FEAT-009d necesita compras confirmadas para que el comprador tenga algo
que ver.

## Goals

- Un participante con cartones en el carrito puede confirmar la compra, registrándose o iniciando
  sesión recién en ese momento (nunca antes, RF-14).
- Si el carrito tiene cartones de más de un organizador, se generan compras independientes, una por
  organizador, cada una con su propio identificador (RF-17a).
- Cada compra registrada queda en estado "pendiente de confirmación de pago", con los datos del
  comprador, el medio de pago elegido y una marca temporal (RF-17b).
- Todo cartón de una compra registrada queda anulado — nunca vuelve a aparecer en descubrimiento ni
  puede venderse dos veces (RF-18, RNF-03).
- Un carrito vacío nunca puede confirmarse (RF-28).

## Functional Requirements

- FR-01: El sistema debe exigir que el participante se registre o inicie sesión con mail y
  contraseña recién al momento de confirmar la compra — nunca antes, durante la navegación,
  selección de cartones o armado del carrito.
- FR-02: El sistema debe requerir, al confirmar la compra, los datos del comprador (apellido,
  nombre, CUIT, mail) y el medio de pago elegido.
- FR-03: El sistema debe ofrecer exactamente dos medios de pago — Efectivo y Transferencia bancaria
  — permitiendo seleccionar uno solo por confirmación de carrito.
- FR-04: El sistema debe rechazar la confirmación de compra si el carrito de la sesión está vacío,
  informando que debe agregar al menos un cartón.
- FR-05: El sistema debe agrupar, al confirmar, los cartones del carrito por organizador, generando
  una compra independiente por cada organizador presente en el carrito, cada una con su propio
  identificador.
- FR-06: El sistema debe asignar a cada compra generada sus cartones correspondientes, los datos del
  comprador, una marca temporal y el medio de pago seleccionado, dejándola en estado "pendiente de
  confirmación de pago" como unidad completa.
- FR-07: El sistema debe anular todo cartón perteneciente a una compra recién registrada, para que
  no esté disponible en ninguna búsqueda ni selección futura (descubrimiento global, por organizador,
  ni nueva tanda).
- FR-08: El sistema debe garantizar que la confirmación de compra sea atómica: si la reserva de
  Redis de uno o más cartones del carrito expiró antes de completarse la confirmación, la operación
  se rechaza por completo (ninguna compra parcial) e informa qué cartones ya no están disponibles.
- FR-09: El sistema debe vaciar el carrito de la sesión (Redis) tras una confirmación exitosa.

## Non-Functional Requirements

- NFR-01: La confirmación de compra debe ser fuertemente consistente: un mismo cartón nunca puede
  terminar asignado a dos compras distintas, verificado con al menos una prueba de confirmación
  concurrente sobre carritos que comparten un cartón (RNF-03 del PRD maestro).
- NFR-02: Los datos personales del comprador (CUIT, nombre, apellido, mail) deben almacenarse con
  acceso restringido por rol — un comprador nunca puede leer datos de otro comprador ni de sus
  compras (RNF-04/RNF-09 del PRD maestro).

## Acceptance Criteria

- AC-01 (FR-01): IF un participante sin autenticar intenta confirmar la compra, THEN THE sistema
  SHALL exigirle registrarse o iniciar sesión antes de continuar, sin registrar la compra hasta que
  lo haga.
- AC-02 (FR-04): IF el carrito de la sesión está vacío, THEN THE sistema SHALL rechazar la
  confirmación e informar que debe agregar al menos un cartón.
- AC-03 (FR-05, FR-06): WHEN un comprador autenticado confirma un carrito con cartones de 2
  organizadores distintos, THE sistema SHALL generar 2 compras independientes, cada una con su
  propio identificador, mismos datos de comprador y mismo medio de pago.
- AC-04 (FR-02, FR-06): WHEN un comprador confirma la compra con apellido, nombre, CUIT, mail y
  medio de pago, THE sistema SHALL persistir la compra con esos datos, una marca temporal, y estado
  "pendiente de confirmación de pago".
- AC-05 (FR-03): WHEN un comprador selecciona "Efectivo" como medio de pago, THE sistema SHALL
  registrar la compra con ese medio de pago, mismo estado que con "Transferencia".
- AC-06 (FR-07): WHEN se registra una compra, THE sistema SHALL anular todos sus cartones — ninguno
  vuelve a aparecer en descubrimiento global, por organizador, ni en una nueva tanda del carrito.
- AC-07 (FR-08): IF la reserva de Redis de al menos un cartón del carrito expiró antes de completar
  la confirmación, THEN THE sistema SHALL rechazar la confirmación completa (sin crear ninguna
  compra parcial) e informar qué cartones ya no están disponibles.
- AC-08 (FR-08, NFR-01): IF dos sesiones distintas intentan confirmar, en el mismo instante, carritos
  que comparten un cartón, THEN THE sistema SHALL completar exitosamente solo una de las dos
  confirmaciones para ese cartón — verificable con una prueba de concurrencia real.
- AC-09 (FR-09): WHEN una confirmación de compra se completa exitosamente, THE sistema SHALL vaciar
  el carrito de esa sesión.

## Out of Scope

- Envío del mail de confirmación con los cartones adjuntos en PDF, y sus reintentos (RF-19a, RF-19b,
  RF-29) — ticket FEAT-009b.
- Confirmación manual de pago por el organizador, cancelación de compra pendiente, liberación de sus
  cartones y mail de cancelación (RF-17c, RF-17d, RF-17e, RF-17f) — ticket FEAT-009c.
- Vista "mis cartones" del comprador, descarga de PDF, actualización de datos de cuenta con bloqueo
  por proximidad de sorteo (RF-20a, RF-20b, RF-21, RF-21b) — ticket FEAT-009d.
- Dashboard del organizador (RF-22, RF-23, RF-24) — ticket futuro, no parte del split de FEAT-009.
- Validación de cartón vendido por GUID (RF-06) — pospuesta desde FEAT-006; queda desbloqueada
  técnicamente por este ticket (ya existe `Compra`) pero no se implementa acá.
- Cualquier pantalla de confirmación de compra en el frontend — backend-only, mismo criterio que el
  resto de los tickets de este roadmap.

## Risks and Mitigations

- **Riesgo:** el comprador es la primera cuenta del proyecto que NO es un organizador — si se
  modela mal (ej. reusando el mismo rol sin distinción), un comprador autenticado podría terminar
  con acceso a endpoints de organizador o viceversa. **Mitigación:** el mecanismo concreto (mismo
  `ApplicationUser` con un rol distinto, vs. una entidad separada) se decide en PLAN, pero la
  invariante — un comprador nunca accede a datos de organizador y viceversa — es un requisito no
  negociable (NFR-02) que el threat model de PLAN debe verificar explícitamente.
- **Riesgo:** la reserva de Redis (FEAT-008b) garantiza que un cartón no se reserve dos veces
  mientras está en un carrito activo, pero no garantiza nada una vez que esa reserva expira — un
  carrito podría intentar confirmarse justo cuando su reserva vence. **Mitigación:** FR-08/AC-07/
  AC-08 exigen rechazar la confirmación completa si la reserva ya no es válida al momento de
  persistir la compra; el mecanismo concreto (revalidar contra Redis dentro de la misma operación
  atómica, o un lock adicional a nivel SQL) se decide en PLAN.
- **Riesgo:** agrupar el carrito por organizador y crear varias compras en una sola confirmación
  introduce una operación multi-entidad — si falla a mitad de camino podría dejar compras parciales
  (ej. la compra del Organizador A creada, la del Organizador B no). **Mitigación:** FR-08 exige
  atomicidad de "todo o nada" sobre la confirmación completa, no por compra individual.

## Dependencies

- `Carrito`/reserva en Redis (FEAT-008b, ya en `main`) — origen de los cartones a confirmar.
- `Bingo`/`Carton` (Domain, FEAT-003) — el cartón anulado pertenece a un `Bingo` existente.
- Identity de organizador (FEAT-001a/b, ya en `main`) — precedente de cómo se modela una cuenta con
  mail+contraseña; el comprador es la primera cuenta de un rol distinto.
