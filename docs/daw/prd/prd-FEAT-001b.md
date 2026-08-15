# PRD FEAT-001b: Login de organizador

| Field | Value |
|-------|-------|
| Ticket | FEAT-001b |
| Tracker | none |
| Date | 2026-08-15 |
| PRD loops | 1 |

## Context and Problem

Una vez que un organizador tiene una cuenta creada (FEAT-001a), necesita poder autenticarse para
acceder al resto de la plataforma: crear bingos, ver su dashboard, confirmar pagos (RF-02 en
adelante del PRD maestro `docs/daw/prd/prd-bingocartV2.md`). Sin login, la cuenta creada en
FEAT-001a no tiene ninguna utilidad.

Este ticket es el sub-ticket `b` del split de FEAT-001 (ver `docs/daw/prd/prd-FEAT-001.md`, índice)
e implementa exclusivamente la autenticación: login con mail y contraseña, emisión de JWT, y
mitigación de fuerza bruta mediante lockout de cuenta.

Referencia: RF-01b, AC-20 (parcial) de `docs/daw/prd/prd-bingocartV2.md`.

## Goals

| # | Objetivo | Métrica de éxito |
|---|----------|-------------------|
| G1 | Permitir a un organizador registrado autenticarse y obtener una sesión utilizable por el resto de la plataforma | Login exitoso emite un JWT válido en < 1 s |
| G2 | Mitigar ataques de fuerza bruta sobre credenciales de organizador | Bloqueo de cuenta tras 5 intentos fallidos consecutivos |
| G3 | Acotar la ventana de exposición de una sesión comprometida | Expiración de JWT a los 60 minutos exactos |

## Functional Requirements

- FR-01: El sistema debe permitir a un organizador registrado autenticarse con mail y contraseña.
- FR-02: El sistema debe emitir, al autenticarse exitosamente, un JWT con una expiración de 60
  minutos desde su emisión.
- FR-03: El sistema debe bloquear la cuenta de un organizador durante 5 minutos después de 5
  intentos de login fallidos consecutivos, rechazando cualquier intento de login durante ese lapso
  aunque la contraseña sea correcta.

## Non-Functional Requirements

- NFR-01: El JWT emitido en el login debe expirar exactamente a los 60 minutos de su emisión, 0
  tokens aceptados por el sistema después de ese plazo, verificable por prueba automatizada.
- NFR-02: El endpoint de login debe responder en menos de 1 segundo p95, incluyendo la emisión del
  JWT.

## Acceptance Criteria

- AC-01 (FR-01, FR-02): WHEN un organizador registrado envía mail y contraseña correctos y la
  cuenta no está bloqueada, THE sistema SHALL autenticarlo y devolver un JWT válido por 60 minutos.
- AC-02 (FR-01): IF un organizador envía una contraseña incorrecta, THEN THE sistema SHALL rechazar
  el login sin indicar si el mail existe o no, e incrementar el contador de intentos fallidos de esa
  cuenta.
- AC-03 (FR-03): WHILE una cuenta de organizador acumula 5 intentos de login fallidos consecutivos,
  THE sistema SHALL bloquear esa cuenta durante 5 minutos, rechazando cualquier intento de login en
  ese lapso aunque la contraseña sea correcta.
- AC-04 (FR-02): WHEN pasan los 60 minutos de expiración de un JWT emitido, THE sistema SHALL
  rechazar cualquier solicitud autenticada que use ese token, exigiendo un nuevo login.

## Out of Scope

- Registro/creación de la cuenta de organizador — implementado en FEAT-001a (dependencia).
- Recuperación de contraseña ("olvidé mi contraseña") — ticket posterior.
- Revocación anticipada de JWT (logout invalidando el token del lado del servidor) — no forma parte
  de este ticket; el JWT expira solo por tiempo (FR-02).
- Refresh tokens / renovación de sesión sin nuevo login — no forma parte de este ticket.
- Autenticación con redes sociales, SSO o 2FA (fuera de alcance del producto completo).
- Rate-limiting por IP (más allá del lockout por cuenta de FR-03).
- Cualquier funcionalidad posterior al login (creación de bingos, dashboard — RF-02 en adelante del
  PRD maestro).

## Risks and Mitigations

| # | Riesgo | Impacto | Mitigación |
|---|--------|---------|------------|
| R-01 | **Fuerza bruta distribuida**: un atacante que rota IPs puede intentar 5 contraseñas por cuenta antes de que el lockout por cuenta se active, y repetir contra muchas cuentas sin activar ningún lockout individual más de una vez por rotación. | Medio — acceso no autorizado si acierta la contraseña dentro del margen de 5 intentos. | El lockout por cuenta (AC-03) es la mitigación de este ticket. Rate-limiting por IP queda fuera de alcance y puede evaluarse en un ticket de hardening posterior. |
| R-02 | **JWT robado dentro de la ventana de 60 minutos** permite suplantar al organizador hasta que expire, ya que este ticket no implementa revocación anticipada. | Medio — acceso no autorizado a los datos del organizador (bingos, compradores) durante la ventana de validez. | Expiración corta (60 min, NFR-01) acota la ventana. HTTPS obligatorio en todo el tráfico de la plataforma (asunción de infraestructura). Revocación de tokens queda fuera de alcance de este ticket. |

## Dependencies

Depende de **FEAT-001a** (registro de organizador): requiere que exista una cuenta de organizador
activa, creada y persistida por ese ticket, para poder autenticarla. No se puede implementar ni
verificar este ticket sin FEAT-001a ya mergeado o disponible en la misma rama base.
