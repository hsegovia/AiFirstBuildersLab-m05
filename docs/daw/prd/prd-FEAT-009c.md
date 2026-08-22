# PRD FEAT-009c: Confirmación y cancelación manual de pago

| Field | Value |
|-------|-------|
| Ticket | FEAT-009c |
| Tracker | none |
| Date | 2026-08-22 |
| PRD loops | 0 |

## Context and Problem

FEAT-009a (`main`) registra cada compra en estado `PendienteConfirmacionPago` — el sistema nunca
procesa pagos automáticamente (Efectivo/Transferencia son siempre conciliación manual del
organizador, fuera de alcance del proyecto). Hoy no existe ningún mecanismo para que esa compra
avance de estado: el organizador no tiene forma de confirmar que recibió el pago, ni de cancelar una
compra que nunca se pagó — los cartones de una compra pendiente quedan bloqueados indefinidamente
(riesgo R-09 del PRD maestro), y el comprador nunca se entera si su compra fue cancelada.

Este ticket cierra ese hueco: dos acciones manuales del organizador sobre sus propias compras
(confirmar pago, cancelar) más el efecto que cada una produce (liberar cartones al cancelar, avisar
al comprador por mail).

**Dependencia con el dashboard (RF-22/23/24):** el PRD maestro describe estas acciones como
ejecutadas "desde el dashboard" del organizador, pero el dashboard completo (conteo de vendidos,
listado con datos del comprador, desglose por medio de pago) es un ticket separado del split de
FEAT-009, sin fecha. Sin ningún listado, confirmar/cancelar no son accionables: el organizador no
tendría cómo saber qué `compraId` existen. Este PRD agrega un listado **mínimo** —solo lo necesario
para hacer las dos acciones operables— dejando el dashboard completo (RF-22, RF-23, RF-24) para su
propio ticket futuro.

## Goals

- El organizador puede confirmar manualmente que recibió el pago de una compra propia, avanzándola a
  estado `Confirmado`.
- El organizador puede cancelar manualmente una compra propia que nunca fue pagada.
- Cancelar una compra libera sus cartones para que vuelvan a estar disponibles en toda consulta de
  descubrimiento/selección, sin perder el registro histórico de que existió esa compra.
- El comprador es notificado por mail cuando su compra es cancelada.
- Ninguna de las dos acciones puede aplicarse dos veces ni sobre una compra en el estado incorrecto:
  un intento inválido se rechaza explícitamente, nunca se ignora en silencio.

## Functional Requirements

- FR-01: El sistema debe permitir a un organizador autenticado confirmar el pago de una compra
  propia que está en estado `PendienteConfirmacionPago`, transicionándola a `Confirmado`.
- FR-02: El sistema debe rechazar con un error explícito el intento de confirmar una compra que no
  está en estado `PendienteConfirmacionPago` (ya `Confirmado` o ya `Cancelado`), sin modificar su
  estado.
- FR-03: El sistema debe permitir a un organizador autenticado cancelar una compra propia que está
  en estado `PendienteConfirmacionPago`, transicionándola a `Cancelado`.
- FR-04: El sistema debe rechazar con un error explícito el intento de cancelar una compra que no
  está en estado `PendienteConfirmacionPago` (ya `Confirmado` o ya `Cancelado`), sin modificar su
  estado.
- FR-05: El sistema debe considerar disponibles para la venta los cartones de una compra en estado
  `Cancelado`, en toda consulta de descubrimiento/selección de cartones, sin eliminar el registro de
  que esos cartones pertenecieron a esa compra.
- FR-06: El sistema debe encolar un mail al comprador notificando la cancelación cada vez que una
  compra pasa a estado `Cancelado`.
- FR-07: El sistema debe permitir a un organizador autenticado listar, de forma paginada, las
  compras registradas sobre sus propios bingos: `compraId`, estado, monto total y fecha de creación
  de cada una — sin datos del comprador ni desglose por medio de pago (eso pertenece al dashboard
  completo, RF-22/RF-23/RF-24, fuera de este ticket).
- FR-08: El sistema debe responder con el mismo error (404) tanto si la compra sobre la que se
  intenta confirmar o cancelar no existe, como si existe pero pertenece a otro organizador, sin
  distinguir ambos casos.

## Non-Functional Requirements

- NFR-01: El listado de compras del organizador (FR-07) debe estar paginado con un tamaño de página
  máximo de 50 elementos, mismo límite ya aplicado en `BingoRepository.ListarPorOrganizadorAsync`
  (FEAT-004).
- NFR-02: Los endpoints de confirmar y cancelar deben aplicar rate limiting de 30 requests/5 min por
  organizador autenticado, mismo orden de magnitud ya usado para endpoints de escritura autenticados
  del proyecto (`"compras"`: 10 req/5min; este es de menor riesgo por estar acotado a recursos ya
  propios del organizador, de ahí el límite más alto).
- NFR-03: `organizadorId` se deriva exclusivamente del claim `NameIdentifier` del JWT ya validado —
  nunca de un parámetro de ruta, query o body, mismo patrón ya aplicado en
  `ComprasController`/`BingosController` (mitigación de IDOR).

## Acceptance Criteria

- AC-01 (FR-01): WHEN un organizador autenticado confirma el pago de una compra propia que está en
  `PendienteConfirmacionPago`, THE sistema SHALL transicionarla a `Confirmado`.
- AC-02 (FR-02): IF un organizador intenta confirmar una compra que no está en
  `PendienteConfirmacionPago`, THEN THE sistema SHALL rechazar la operación con un error explícito y
  dejar el estado sin cambios.
- AC-03 (FR-03): WHEN un organizador autenticado cancela una compra propia que está en
  `PendienteConfirmacionPago`, THE sistema SHALL transicionarla a `Cancelado`.
- AC-04 (FR-04): IF un organizador intenta cancelar una compra que no está en
  `PendienteConfirmacionPago`, THEN THE sistema SHALL rechazar la operación con un error explícito y
  dejar el estado sin cambios.
- AC-05 (FR-05): WHEN una compra pasa a `Cancelado`, THE sistema SHALL hacer que sus cartones vuelvan
  a aparecer como disponibles en cualquier consulta de descubrimiento o selección de cartones.
- AC-06 (FR-06): WHEN una compra pasa a `Cancelado`, THE sistema SHALL encolar un mail al comprador
  notificando la cancelación.
- AC-07 (FR-07): WHEN un organizador autenticado solicita el listado de sus compras, THE sistema
  SHALL devolver, paginado, `compraId`, estado, monto total y fecha de creación de cada una, sin
  incluir datos del comprador.
- AC-08 (FR-08): IF un organizador intenta confirmar, cancelar o consultar una compra que no existe
  o que pertenece a otro organizador, THEN THE sistema SHALL responder 404, sin distinguir entre
  ambos casos.

## Out of Scope

- Dashboard completo del organizador — conteo de vendidos vs. totales (RF-22), listado con datos del
  comprador (RF-23), desglose de ventas por medio de pago (RF-24). Ticket futuro, separado, fuera del
  split de FEAT-009 (ya documentado así en `prd-FEAT-009a.md`).
- Cancelación de una compra ya en estado `Confirmado` (flujo de reembolso) — no mencionado en el PRD
  maestro para esta funcionalidad.
- FEAT-009d ("mis cartones" del comprador — RF-20a, RF-20b, RF-21, RF-21b) — sub-ticket separado del
  split.
- Cualquier pantalla de organizador en el frontend — backend-only, mismo criterio que el resto del
  roadmap.
- Procesamiento automático de pagos — sigue siendo, como en todo el proyecto, conciliación manual del
  organizador (Fuera de Alcance del PRD maestro, `AGENTS.md`).

## Risks and Mitigations

- **Las 4 queries de disponibilidad existentes no distinguen el estado de la compra**:
  `BingoRepository.cs` (líneas 62 y 79) y `DescubrimientoRepository.cs` (líneas 60 y 93) marcan un
  cartón como vendido solo por tener una fila en `CompraCartones`, sin mirar `Compra.Estado`.
  Mitigación: las 4 se modifican para excluir compras en `Cancelado` (decisión ya tomada: derivar
  disponibilidad en query-time, nunca borrar filas de `CompraCartones`) — impacto verificado contra
  el código actual, no especulativo, y a validar en el Impact Scan de PLAN.
- **Confirmar/cancelar sobre una compra en el estado equivocado corrompería el flujo de ventas**
  (ej. cancelar una compra ya confirmada liberaría cartones ya cobrados): mitigado por diseño
  (FR-02/FR-04, AC-02/AC-04) — cualquier transición fuera de `PendienteConfirmacionPago` se rechaza
  explícitamente, nunca se aplica silenciosamente.
- **El mail de cancelación depende de la infraestructura de FEAT-009b**: ya mergeada a `main`
  (outbox en SQL + `BackgroundService`, reintentos hasta 3 con 1 min entre intentos) — este ticket la
  reutiliza, no construye un mecanismo nuevo.

## Dependencies

- FEAT-009a (`Compra`, `EstadoCompra` con los 3 valores ya definidos, `CompraCarton` — ya en `main`).
  Este ticket no migra el esquema de `EstadoCompra`: los valores `Confirmado`/`Cancelado` ya existen,
  fueron definidos en FEAT-009a precisamente para no requerir una segunda migración.
- FEAT-009b (`IEnvioMailService`/`EnvioMailRepository`/`MailKitEmailSender`/
  `EnvioMailBackgroundService`, ya en `main`) — RF-06 (mail de cancelación) reutiliza esta
  infraestructura.
