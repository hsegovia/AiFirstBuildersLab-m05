# PRD FEAT-010a: Infraestructura transversal y resolución de rol (whoami)

| Field | Value |
|-------|-------|
| Ticket | FEAT-010a |
| Tracker | none |
| Date | 2026-08-25 |
| PRD loops | 0 |

## Context and Problem

Split 1 de 5 de FEAT-010 (`docs/daw/prd/prd-FEAT-010.md`, ahora índice). El frontend Angular hoy
solo tiene registro y login de organizador (`frontend/src/app/features/auth/`), sin guards, sin
resolución de rol, y con un interceptor HTTP (`core/interceptors/http-error.interceptor.ts`) que
solo maneja 5xx y errores de red — no 401/403/429. La sesión de organizador se guarda en un
`BehaviorSubject` en memoria, documentado en el propio código como un gap conocido: se pierde en
cada refresh de página.

La cookie de autenticación (`bingocart_auth`) es `httpOnly`, así que el JavaScript del cliente no
puede leer el rol del JWT directamente. Este sub-ticket agrega el único endpoint de backend que este
frontend necesita (`GET /api/auth/whoami`) y construye, encima de él, el interceptor extendido, el
servicio de sesión y los guards de rol que **todos los demás sub-tickets de FEAT-010 (b, c, d, e)
dependen**.

## Goals

- Que el frontend sepa, en cualquier momento (incluido un refresh de página), si hay una sesión
  activa y de qué rol, sin exponer el JWT al JavaScript del cliente.
- Que cualquier ruta protegida rechace el acceso al rol equivocado antes de intentar la llamada a la
  API.
- Que ningún error HTTP (401, 403, 429) deje una pantalla en blanco o un estado indefinido.

## Functional Requirements

- FR-01: El sistema debe exponer `GET /api/auth/whoami`, autenticado (cualquier rol), que devuelve
  `{Rol, Mail}` leyendo los claims del JWT vigente — sin nueva tabla, sin nuevo estado, solo lectura
  del token ya emitido.
- FR-02: El sistema debe presentar un layout base con navegación que varíe según el estado de
  sesión: anónimo, organizador autenticado, o comprador autenticado — resuelto vía FR-01 al
  arrancar la aplicación.
- FR-03: El sistema debe manejar todos los errores HTTP mediante un interceptor centralizado
  (401 → redirección a login, 403 → mensaje de permiso denegado, 429 → mensaje de límite excedido,
  5xx → mensaje genérico, ya existente), nunca un `.subscribe()` sin manejador de error.
- FR-04: El sistema debe restringir el acceso a las rutas de organizador y a las rutas de comprador
  mediante guards que consulten el rol resuelto por FR-01/FR-02, redirigiendo a login si no
  corresponde.

## Non-Functional Requirements

- NFR-01: Ningún dato de sesión sensible (contraseña, JWT) se persiste en `localStorage` ni
  `sessionStorage` — la sesión sobrevive a un refresh exclusivamente vía la cookie `httpOnly` y el
  endpoint de FR-01.
- NFR-02: El componente de paginación compartido que usarán FEAT-010b/c/e se construye en este
  sub-ticket, como único componente reutilizable — no 3 implementaciones distintas más adelante.
- NFR-03: Cada error 429 recibido por el interceptor de FR-03 se muestra como mensaje genérico, sin
  sugerir un tiempo de espera específico (el backend no expone `Retry-After` en ninguna de sus 9
  políticas de rate limiting).
- NFR-04: El layout base es usable en viewport móvil (≥360px de ancho).

## Acceptance Criteria

*(EARS — ver `.daw/rules/validation-rules.instructions.md` §1)*

- AC-01: WHEN un cliente autenticado (organizador o comprador) llama a `GET /api/auth/whoami`, THE
  sistema SHALL devolver `{Rol, Mail}` correspondiente a los claims del JWT vigente (FR-01).
- AC-02: IF `GET /api/auth/whoami` se llama sin una cookie de autenticación válida, THEN THE sistema
  SHALL devolver 401 sin exponer ningún dato de cuenta (FR-01).
- AC-03: WHEN la sesión se resuelve como organizador o como comprador, THE sistema SHALL mostrar la
  navegación correspondiente a ese rol (FR-02).
- AC-04: WHEN la sesión se refresca (recarga de página) con una cookie de autenticación vigente, THE
  sistema SHALL restaurar el layout correspondiente al rol vía FR-01, sin redirigir a login (FR-01,
  FR-02).
- AC-05: IF la cookie de autenticación no existe o expiró al refrescar una ruta protegida, THEN THE
  sistema SHALL redirigir a la pantalla de login correspondiente, preservando la ruta de destino
  para volver tras autenticarse (FR-04).
- AC-06: IF cualquier llamada a la API devuelve 401, THEN THE sistema SHALL redirigir a la pantalla
  de login correspondiente al rol esperado por la ruta actual (FR-03).
- AC-07: IF cualquier llamada a la API devuelve 403, THEN THE sistema SHALL mostrar un mensaje de
  permiso denegado sin redirigir a login (FR-03).
- AC-08: IF cualquier llamada a la API devuelve 429, THEN THE sistema SHALL mostrar un mensaje de
  límite excedido sin dejar la pantalla en blanco ni la acción en un estado indefinido (FR-03).
- AC-09: IF un usuario autenticado como organizador intenta acceder a una ruta de comprador (o
  viceversa), THEN THE sistema SHALL redirigir a login sin ejecutar ninguna llamada a la API
  protegida de ese rol (FR-04).

## Out of Scope

- **Ninguna pantalla de negocio** (bingos, descubrimiento, carrito, checkout, mis cartones, cuenta)
  — eso es FEAT-010b/c/d/e, que dependen de este sub-ticket.
- **Registro/login de comprador** — tiene su propio flujo, construido en FEAT-010d; este sub-ticket
  solo construye la infraestructura que ese flujo va a usar (guard, interceptor, whoami).
- **Revocación de sesión / invalidación de tokens** — el proyecto usa JWT Bearer stateless sin
  `SecurityStampValidator` (ver `docs/daw/security/threat-FEAT-009d.md`, R-02); whoami es
  puramente de lectura, no cambia ese comportamiento.
- **Retry automático tras un 429** — NFR-03 exige el mensaje, no un backoff automático.

## Risks and Mitigations

- **R-01 — `whoami` es la única superficie de backend de un ticket clasificado como frontend.**
  Mitigación: es de solo lectura sobre el JWT ya emitido, sin nuevo estado ni migración. El threat
  model de PLAN debe confirmar que la respuesta nunca incluye el JWT completo ni el
  `NameIdentifier`, solo `Rol`/`Mail`.
- **R-02 — Nueve políticas de rate limiting distintas** (`carrito`, `compras`, `compras-organizador`,
  `comprador-cuenta`, `bingos`, `registro`, `compradores`, `directorio`, `descubrimiento`), cada una
  con su propio límite. NFR-03 exige manejarlas todas de forma genérica desde un único interceptor,
  sin que el frontend tenga que conocer el límite específico de cada una.

## Dependencies

- Ninguna hacia atrás — es el primer sub-ticket de FEAT-010.
- FEAT-010b, FEAT-010c, FEAT-010d y FEAT-010e dependen de este sub-ticket (guard, interceptor,
  whoami, componente de paginación compartido).
- Backend: `JwtTokenService` (`backend/BingoCart.Infrastructure/Auth/JwtTokenService.cs`) y el
  pipeline de autenticación en `Program.cs` (lectura del JWT desde la cookie `bingocart_auth` vía
  `OnMessageReceived`) — ya existen, sin cambios.
