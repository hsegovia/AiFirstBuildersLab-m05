# PRD FEAT-010b: Organizador — gestión de bingos y conciliación de ventas

| Field | Value |
|-------|-------|
| Ticket | FEAT-010b |
| Tracker | none |
| Date | 2026-08-25 |
| PRD loops | 0 |

## Context and Problem

Split 2 de 5 de FEAT-010 (`docs/daw/prd/prd-FEAT-010.md`). El backend de gestión de bingos
(`BingosController`) y de conciliación manual de pago (`ComprasController`, endpoints de
organizador) está completo desde FEAT-003, FEAT-007 y FEAT-009c, pero no tiene pantalla. Este
sub-ticket construye el dashboard mínimo del organizador: crear, listar, editar y eliminar sus
bingos, y confirmar o cancelar manualmente el pago de sus compras.

Depende de FEAT-010a (guard de rol Organizador, interceptor, componente de paginación
compartido) — ya mergeado antes de empezar este sub-ticket.

## Goals

- Que un organizador pueda crear un bingo y ver el listado de los suyos sin usar la API a mano.
- Que un organizador pueda corregir o dar de baja un bingo mientras no tenga ventas.
- Que un organizador pueda conciliar manualmente el pago de sus compras (confirmar o cancelar),
  sin exponer datos del comprador en el listado.

## Functional Requirements

- FR-01: El sistema debe permitir a un organizador autenticado crear un bingo indicando nombre del
  evento, fecha y hora del sorteo, cantidad de cartones y costo por cartón.
- FR-02: El sistema debe permitir a un organizador autenticado listar, paginados, los bingos que él
  mismo creó.
- FR-03: El sistema debe permitir a un organizador autenticado editar el nombre del evento, la
  fecha de sorteo y el costo por cartón de un bingo sin compras registradas.
- FR-04: El sistema debe permitir a un organizador autenticado eliminar un bingo sin compras
  registradas, con una confirmación explícita antes de ejecutar la acción.
- FR-05: El sistema debe permitir a un organizador autenticado listar, paginadas, las compras
  generadas sobre sus bingos, sin exponer datos del comprador en el listado.
- FR-06: El sistema debe permitir a un organizador autenticado confirmar manualmente el pago de una
  compra propia en estado pendiente.
- FR-07: El sistema debe permitir a un organizador autenticado cancelar una compra propia en estado
  pendiente.

## Non-Functional Requirements

- NFR-01: La validación del formulario de bingo en cliente refleja, sin duplicar como regla de
  negocio propia, las mismas reglas que el backend ya aplica — el mensaje de error mostrado se
  deriva del código que el backend ya devuelve (`CantidadCartonesExcedeLimite`, `FechaSorteoInvalida`,
  `CostoPorCartonInvalido`), no de una reimplementación paralela.
- NFR-02: Los dos listados de este sub-ticket (bingos propios, compras propias) usan el componente
  de paginación compartido de FEAT-010a — no una implementación propia.
- NFR-03: Las pantallas son usables en viewport móvil (≥360px de ancho).

## Acceptance Criteria

*(EARS — ver `.daw/rules/validation-rules.instructions.md` §1)*

- AC-01: WHEN un organizador autenticado envía el formulario de creación de bingo con datos
  válidos, THE sistema SHALL crear el bingo y navegar al listado de bingos propios mostrándolo
  (FR-01).
- AC-02: IF la creación de un bingo falla por `CantidadCartonesExcedeLimite`, `FechaSorteoInvalida`
  o `CostoPorCartonInvalido`, THEN THE sistema SHALL mostrar el mensaje de error específico sin
  perder los datos ya ingresados en el formulario (FR-01).
- AC-03: WHEN un organizador autenticado abre el listado de bingos propios, THE sistema SHALL
  mostrarlos paginados con el componente de NFR-02 (FR-02).
- AC-04: WHEN un organizador autenticado edita un bingo sin compras registradas con datos válidos,
  THE sistema SHALL actualizar el bingo y reflejarlo en el listado (FR-03).
- AC-05: IF un organizador intenta eliminar un bingo con compras registradas, THEN THE sistema
  SHALL mostrar el motivo del rechazo (409 `BingoConCompras`) sin ejecutar ninguna eliminación
  (FR-04).
- AC-06: WHEN un organizador autenticado abre el listado de sus compras, THE sistema SHALL
  mostrarlas paginadas sin ningún dato del comprador en la respuesta ni en la pantalla (FR-05).
- AC-07: WHEN un organizador autenticado confirma el pago de una compra pendiente propia, THE
  sistema SHALL reflejar el nuevo estado "Confirmado" en el listado sin recargar la página completa
  (FR-06).
- AC-08: IF un organizador intenta confirmar o cancelar una compra que no está en estado pendiente,
  THEN THE sistema SHALL mostrar el 409 `EstadoInvalido` como mensaje, sin cambiar el estado
  mostrado (FR-06, FR-07).

## Out of Scope

- **Dashboard de ventas con datos del comprador y desglose por medio de pago (RF-22/23/24 del PRD
  maestro)** — el backend no existe, ver `docs/daw/prd/prd-FEAT-009d.md`, Out of Scope.
- **Descubrimiento, carrito, checkout, mis cartones, cuenta del comprador** — FEAT-010c/d/e.
- **Registro/login de organizador** — ya tiene pantalla, construida en FEAT-001a/b, no se toca.
- **Validación de cartón por GUID (RF-06 del PRD maestro)** — sin ticket propio.

## Risks and Mitigations

- **R-01 — Cantidad de cartones no es editable** (`PUT /api/bingos/{id}` no acepta ese campo). El
  formulario de edición debe mostrarlo como solo lectura o directamente omitirlo, para no sugerir
  al organizador que puede cambiarlo.
- **R-02 — Aislamiento entre FR-06/FR-07 y el listado de FR-05**: confirmar o cancelar un pago debe
  reflejarse en el listado sin depender de que el organizador recargue la página completa, para no
  perder la posición de paginación en la que estaba.

## Dependencies

- **FEAT-010a** (infraestructura transversal) — guard de rol Organizador, interceptor HTTP,
  componente de paginación compartido. Debe estar mergeado antes de empezar.
- Backend: `BingosController`, `ComprasController` (endpoints de organizador) — ya existen en
  `main` desde FEAT-003, FEAT-007 y FEAT-009c, sin cambios.
