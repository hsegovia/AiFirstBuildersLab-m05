# PRD FEAT-010d: Comprador — checkout (registro/login y confirmación de compra)

| Field | Value |
|-------|-------|
| Ticket | FEAT-010d |
| Tracker | none |
| Date | 2026-08-25 |
| PRD loops | 0 |

## Context and Problem

Split 4 de 5 de FEAT-010 (`docs/daw/prd/prd-FEAT-010.md`). El backend de registro/login de
comprador (FEAT-009a) y de confirmación de compra (FEAT-009a) está completo, pero sin pantalla.
Este sub-ticket cierra el punto de conversión del recorrido del comprador: con un carrito ya armado
(FEAT-010c), exige registrarse o iniciar sesión recién en este momento (RF-14 del PRD maestro — no
antes), y confirma la compra agrupando por organizador.

Depende de FEAT-010a (guard de rol Comprador, interceptor) y de FEAT-010c (el carrito tiene que
existir para que haya algo que confirmar).

## Goals

- Que un visitante con carrito armado pueda registrarse o iniciar sesión como comprador sin perder
  el carrito ya armado.
- Que un comprador autenticado pueda confirmar la compra de su carrito eligiendo el medio de pago.
- Que el resultado de la confirmación se muestre agrupado por organizador, con el detalle de
  cartones de cada compra generada.

## Functional Requirements

- FR-01: El sistema debe exigir que el participante inicie sesión o se registre como comprador
  antes de habilitar la confirmación de la compra, sin interrumpir ni descartar el carrito ya
  armado.
- FR-02: El sistema debe permitir el registro de un nuevo comprador (apellido, nombre, CUIT, mail,
  contraseña) desde el flujo de checkout.
- FR-03: El sistema debe permitir a un visitante con cuenta de comprador existente iniciar sesión
  desde el flujo de checkout.
- FR-04: El sistema debe permitir al comprador autenticado confirmar la compra de su carrito
  indicando el medio de pago (Efectivo o Transferencia).
- FR-05: El sistema debe presentar, tras una confirmación exitosa, el resultado agrupado por
  organizador (una compra por organizador), con el detalle de los cartones de cada una.

## Non-Functional Requirements

- NFR-01: La validación del formulario de registro de comprador en cliente refleja las mismas
  reglas que el backend ya aplica (CUIT de 11 dígitos con dígito verificador, formato de mail,
  campos requeridos) — el mensaje de error se deriva del código que el backend ya devuelve, no de
  una reimplementación paralela.
- NFR-02: Ningún dato de la contraseña ingresada en el registro o el login de comprador se persiste
  en `localStorage` ni `sessionStorage`, ni siquiera temporalmente entre los pasos de FR-01.
- NFR-03: Las pantallas son usables en viewport móvil (≥360px de ancho).

## Acceptance Criteria

*(EARS — ver `.daw/rules/validation-rules.instructions.md` §1)*

- AC-01: WHEN un visitante con carrito no vacío intenta confirmar la compra sin sesión de
  comprador, THE sistema SHALL ofrecerle iniciar sesión o registrarse sin descartar el carrito
  armado (FR-01).
- AC-02: WHEN un visitante completa el formulario de registro de comprador con datos válidos, THE
  sistema SHALL crear la cuenta y continuar el flujo de checkout ya autenticado, conservando el
  carrito (FR-02).
- AC-03: WHEN un visitante con cuenta de comprador existente completa el formulario de login con
  credenciales válidas, THE sistema SHALL continuar el flujo de checkout ya autenticado,
  conservando el carrito (FR-03).
- AC-04: WHEN un comprador autenticado confirma la compra de un carrito no vacío con un medio de
  pago válido, THE sistema SHALL mostrar el resultado agrupado por organizador con el detalle de
  cartones de cada compra (FR-04, FR-05).
- AC-05: IF la confirmación de compra falla por `CarritoVacio` (400), THEN THE sistema SHALL
  mostrar el mensaje y ofrecer volver al descubrimiento, sin intentar reenviar la confirmación
  (FR-04).
- AC-06: IF la confirmación de compra falla por `ReservaCarritoInvalida` (409), THEN THE sistema
  SHALL indicar cuáles cartones ya no están disponibles (usando `cartonIdsInvalidos`) y actualizar
  el carrito mostrado para quitarlos (FR-04).

## Out of Scope

- **Descubrimiento y armado del carrito** — FEAT-010c, prerrequisito de este sub-ticket.
- **"Mis cartones" y datos de cuenta** — FEAT-010e, siguiente sub-ticket.
- **Cambio de contraseña del comprador** — sin flujo en el backend.
- **Recuperación de contraseña olvidada** — sin flujo en el backend, para ningún rol.

## Risks and Mitigations

- **R-01 — Perder el carrito al navegar a registro/login.** El carrito vive en una cookie anónima
  distinta de la cookie de autenticación (`bingocart_carrito` vs. `bingocart_auth`); FR-01 debe
  garantizar que el flujo de registro/login no borre ni reemplace esa cookie — es responsabilidad
  del frontend no invocar nada que la toque durante ese paso.
- **R-02 — Orden de las 5 validaciones del checkout** (contraseña de registro, colisión de mail/CUIT
  en registro, carrito vacío, reserva inválida): cada una tiene su propio código de error ya
  definido por el backend; este sub-ticket solo mapea cada código a su mensaje, no decide el orden
  de verificación (eso ya lo decide el backend).

## Dependencies

- **FEAT-010a** (infraestructura transversal) — guard de rol Comprador, interceptor HTTP. Debe
  estar mergeado antes de empezar.
- **FEAT-010c** (descubrimiento y carrito) — el carrito tiene que existir para que FR-01/FR-04
  tengan algo sobre lo cual operar. Debe estar mergeado antes de empezar.
- Backend: `CompradoresController` (registro/login), `ComprasController.Confirmar` — ya existen en
  `main` desde FEAT-009a, sin cambios.
