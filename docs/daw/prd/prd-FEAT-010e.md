# PRD FEAT-010e: Comprador — mis cartones y datos de cuenta

| Field | Value |
|-------|-------|
| Ticket | FEAT-010e |
| Tracker | none |
| Date | 2026-08-25 |
| PRD loops | 0 |

## Context and Problem

Split 5 de 5 de FEAT-010 (`docs/daw/prd/prd-FEAT-010.md`), el último. El backend de "mis cartones"
y actualización de datos de cuenta (FEAT-009d) está completo — aunque su PR #14 todavía no está
mergeado a `main` al momento de escribir este PRD —, pero sin pantalla. Este sub-ticket cierra el
recorrido del comprador: ver y descargar los cartones que compró, y mantener actualizados sus datos
de cuenta.

Depende de FEAT-010a (guard de rol Comprador) y de FEAT-010d (el comprador tiene que poder llegar
autenticado a estas pantallas, típicamente tras confirmar una compra).

## Goals

- Que un comprador autenticado pueda ver todos los cartones que compró, en cualquier estado de
  compra, y descargar el PDF de cualquiera.
- Que un comprador autenticado pueda corregir sus datos de cuenta (apellido, nombre, CUIT, mail)
  demostrando que sigue siendo dueño de la cuenta con su contraseña actual.

## Functional Requirements

- FR-01: El sistema debe permitir al comprador autenticado ver, paginados, los cartones que tiene
  adquiridos, en cualquier estado de compra.
- FR-02: El sistema debe permitir al comprador autenticado descargar el PDF de cualquiera de sus
  cartones adquiridos.
- FR-03: El sistema debe permitir al comprador autenticado actualizar sus datos de cuenta
  (apellido, nombre, CUIT, mail), exigiendo su contraseña actual como confirmación.

## Non-Functional Requirements

- NFR-01: El listado de FR-01 usa el componente de paginación compartido de FEAT-010a — no una
  implementación propia.
- NFR-02: La descarga de PDF de FR-02 se recibe como `blob` (`responseType: 'blob'`) y se entrega al
  navegador sin persistirla en `localStorage`, `sessionStorage` ni IndexedDB.
- NFR-03: Ningún valor del formulario de datos de cuenta (la contraseña actual en particular) se
  persiste en `localStorage` ni `sessionStorage`, ni siquiera temporalmente tras el envío de FR-03.

## Acceptance Criteria

*(EARS — ver `.daw/rules/validation-rules.instructions.md` §1)*

- AC-01: WHEN un comprador autenticado abre "mis cartones", THE sistema SHALL listar, paginados,
  todos sus cartones adquiridos en cualquier estado de compra, incluidas las canceladas (FR-01).
- AC-02: WHEN un comprador autenticado pide el PDF de un cartón propio, THE sistema SHALL
  descargarlo con el `Content-Type` `application/pdf` recibido (FR-02).
- AC-03: WHEN un comprador autenticado envía el formulario de datos de cuenta con la contraseña
  actual correcta y datos válidos, THE sistema SHALL actualizar los datos mostrados y reflejar el
  éxito sin pedir un nuevo login (FR-03).
- AC-04: IF la actualización de datos de cuenta falla por `ContrasenaIncorrecta` (403), THEN THE
  sistema SHALL mostrar el error sin limpiar los campos de apellido/nombre/CUIT/mail ya ingresados,
  y sin revelar si el mail o el CUIT ingresados colisionan con otra cuenta (FR-03).
- AC-05: IF la actualización de datos de cuenta falla por `PlazoModificacionVencido` (409), THEN
  THE sistema SHALL explicar que hay un sorteo dentro de la próxima hora y no permitir reintentar
  el mismo envío sin recargar el estado (FR-03).

## Out of Scope

- **Cambio de contraseña del comprador** — sin flujo en el backend.
- **Descarga de todos los cartones en un único PDF** — el backend solo expone descarga individual
  (`docs/daw/prd/prd-FEAT-009d.md`, Out of Scope).
- **Dashboard del organizador, descubrimiento, carrito, checkout** — FEAT-010b/c/d.

## Risks and Mitigations

- **R-01 — El PDF nunca se persiste en el backend** (mitigación de seguridad ya documentada en
  `docs/daw/security/threat-FEAT-009d.md`, R-03). NFR-02 extiende ese mismo criterio al frontend:
  tampoco se cachea del lado del cliente más allá de la descarga puntual del navegador.
- **R-02 — Mensajes de colisión de mail/CUIT no deben revelar el valor enviado** (mitigación de
  enumeración ya aplicada en el backend, `docs/daw/security/threat-FEAT-009d.md`, R-04); AC-04 exige
  que el frontend tampoco lo infiera ni lo muestre de otra forma (ej. no comparar visualmente el
  valor enviado contra uno "sugerido").

## Dependencies

- **FEAT-010a** (infraestructura transversal) — guard de rol Comprador, componente de paginación
  compartido. Debe estar mergeado antes de empezar.
- **FEAT-010d** (checkout) — el comprador necesita una sesión autenticada para llegar a estas
  pantallas. Debe estar mergeado antes de empezar.
- Backend: `MisCartonesController`, `CompradoresController.ActualizarCuenta` — de FEAT-009d, cuyo
  PR #14 debe estar mergeado a `main` antes del cierre (RELEASE) de este sub-ticket.
