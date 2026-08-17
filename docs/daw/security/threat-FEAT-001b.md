# Threat Model FEAT-001b: Login de organizador

| Field | Value |
|-------|-------|
| Ticket | FEAT-001b |
| Spec | docs/daw/specs/spec-FEAT-001b.md |
| Date | 2026-08-17 |
| Result | PASSED (mitigaciones folded en el spec) |

## Attack surfaces identified

1. **`POST /api/organizadores/login`** (Block 2) — acepta mail+password, único endpoint de la
   plataforma que no requiere autenticación previa por diseño (otorga autenticación).
2. **`JwtTokenService`** (Block 1) — firma HMAC-SHA256, clave de firma (`Jwt:SigningKey`).
3. **Pipeline `AddJwtBearer`** (Block 1) — valida el JWT en cada request a un endpoint protegido,
   extrayéndolo de la cookie `bingocart_auth`.
4. **`GET /api/organizadores/perfil`** (Block 3) — primer endpoint protegido de la plataforma.
5. **`AuthService` (frontend)** (Block 4) — origina la request de login con `withCredentials`.

## Trust boundaries

- Navegador (no confiable) → API (`POST /login`, `GET /perfil`) — boundary existente desde
  FEAT-001a, ahora también transporta la cookie de sesión `bingocart_auth`.
- API → SQL Server (Identity store) — boundary existente, sin cambios.
- **Nuevo**: Servidor (emisor del JWT) → Cookie del navegador (almacenamiento del JWT) → Servidor
  (verificador del JWT en la siguiente request) — el JWT cruza este boundary dos veces por request
  autenticada.

## STRIDE por componente

### `POST /login`

| Categoría | Análisis |
|---|---|
| Spoofing | Un atacante puede intentar impersonar a un organizador probando contraseñas — mitigado por lockout (FR-03, 5 intentos/5 min). |
| Tampering | El body podría alterarse en tránsito — mitigado por HTTPS obligatorio (asunción de infraestructura ya declarada en R-02 del PRD). |
| Repudiation | No hay logging de intentos de login más allá del contador interno de Identity. Riesgo LOW/aceptado — el PRD no exige auditoría de login; se señala para un ticket de hardening futuro, no bloquea este ticket. |
| Information Disclosure | 🟢 Mitigado: el mensaje "Credenciales inválidas" es idéntico para mail inexistente, password incorrecta Y cuenta bloqueada (spec, Error handling) — un 401 nunca confirma por sí solo que una cuenta existe. |
| Denial of Service | 🟡 Ver Risk TM-01 (fuerza bruta distribuida) — accepted risk, ya documentado como R-01 en el PRD. |
| Elevation of Privilege | N/A — un solo rol (organizador) en el sistema. |

### `JwtTokenService` / `Jwt:SigningKey`

| Categoría | Análisis |
|---|---|
| Spoofing | 🟠 Ver Risk TM-02 (clave de firma débil permite forjar tokens de cualquier organizador). |
| Tampering | Mitigado: HMAC-SHA256 detecta cualquier alteración de claims sin conocer la clave. |
| Information Disclosure | Los claims (`NameIdentifier`, `Email`) son legibles por cualquiera con el token (JWT no cifra, solo firma) — riesgo LOW, es el propio organizador quien recibe su token, y esos datos ya los conoce sobre sí mismo. |
| Denial of Service | N/A |
| Elevation of Privilege | N/A |

### Cookie `bingocart_auth` (transporte del JWT)

| Categoría | Análisis |
|---|---|
| Spoofing | 🟡 Ver Risk TM-03 (robo de cookie vía XSS) — mitigado por la decisión de usar `httpOnly`. |
| Tampering | `Secure` fuerza HTTPS en tránsito; `HttpOnly` bloquea lectura/escritura desde JS. |
| Information Disclosure | Mitigado por `HttpOnly` — un script inyectado no puede leer el valor de la cookie (a diferencia de `localStorage`). |
| Denial of Service | N/A |
| Elevation of Privilege | N/A |
| **CSRF** (no es STRIDE puro, pero es la contracara de usar cookies) | 🟢 Mitigado por `SameSite=Strict`: el navegador no envía la cookie en requests cross-site. El único endpoint protegido de este ticket (`GET /perfil`) es de solo lectura, sin efectos secundarios — no se agrega token anti-CSRF adicional en este ticket (ver spec, Block 2, API contract). |

## Risks

| # | Riesgo | STRIDE | Likelihood | Impact | Mitigación |
|---|--------|--------|------------|--------|------------|
| TM-01 | Fuerza bruta distribuida: un atacante que rota IPs puede intentar 5 contraseñas por cuenta antes de activar el lockout, y repetir contra muchas cuentas. | Spoofing / DoS | Medium | Medium | **Riesgo aceptado** (ver abajo) — ya documentado como R-01 en `docs/daw/prd/prd-FEAT-001b.md`, aprobado por el usuario en DEFINE al aprobar el PRD. |
| TM-02 | `Jwt:SigningKey` corta (< 32 bytes) permitiría, en teoría, forzar la clave HMAC-SHA256 y forjar tokens para cualquier organizador sin conocer su password. | Spoofing / Elevation of Privilege | Low (requiere que alguien configure una clave corta) | Critical (compromiso total de autenticación) | **Mitigado**: validación de longitud mínima (32 bytes) al arrancar la app, falla el arranque si no se cumple — folded en Block 1 del spec (Error handling + test dedicado). |
| TM-03 | JWT robado desde `localStorage` vía una futura vulnerabilidad XSS en el frontend, permitiendo impersonar al organizador hasta por 60 minutos. | Spoofing / Information Disclosure | Low hoy (sin superficie XSS conocida) pero el impacto era alto si ocurriera | High (si ocurriera) | **Mitigado por diseño**: decisión del usuario en esta misma sesión de threat modeling — el JWT se transporta en una cookie `httpOnly` (inaccesible desde JS) en vez de en el body/`localStorage`. Elimina el vector por completo, no lo reduce. Costo aceptado: agrega `SameSite=Strict` + `AllowCredentials()` en CORS + lectura de cookie en `AddJwtBearer` (Block 1), documentado en el spec. |
| TM-04 | JWT robado dentro de la ventana de 60 minutos (por cualquier otro vector, ej. malware en el dispositivo) permite impersonar al organizador hasta que expire — este ticket no implementa revocación anticipada. | Spoofing | Low | Medium | **Riesgo aceptado** — ya documentado como R-02 en `docs/daw/prd/prd-FEAT-001b.md` (expiración corta de 60 min como mitigación principal; revocación anticipada explícitamente fuera de alcance). |

## Accepted risks (F-TM-04)

### TM-01 — Fuerza bruta distribuida (sin rate-limiting por IP)
- **Quién lo aceptó**: el usuario, en DEFINE, al aprobar `prd-FEAT-001b.md` (sección Risks and
  Mitigations, R-01) — y reconfirmado en esta sesión de threat modeling sin objeción.
- **Justificación**: rate-limiting por IP es un concern de infraestructura con un patrón de fallo
  distinto (requiere estado compartido entre instancias si el backend escala horizontalmente) que
  el lockout por cuenta; agruparlo en este ticket habría ampliado su alcance más allá de "login +
  lockout por cuenta" que aprobó el PRD.
- **Condición de revisión**: reevaluar si se observan patrones de fuerza bruta distribuida en
  producción, o antes de escalar el backend a múltiples instancias (momento en el que el
  rate-limiting actual del proyecto, si lo hay a nivel de IP, necesitaría revisarse de todos modos).

### TM-04 — Sin revocación anticipada de JWT
- **Quién lo aceptó**: el usuario, en DEFINE, al aprobar `prd-FEAT-001b.md` (sección Risks and
  Mitigations, R-02, y "Out of Scope").
- **Justificación**: implementar una blocklist de tokens revocados requiere estado compartido
  (Redis u otro store) que no está en el alcance de este ticket; la expiración de 60 minutos acota
  la ventana de exposición a un nivel razonable para el riesgo actual del producto (MVP).
- **Condición de revisión**: reevaluar si el producto empieza a manejar datos más sensibles
  (pagos, por ejemplo) o si se reporta un incidente de robo de sesión.

## Sensitive data classification (F-TM-05)

- **Credentials**: password del organizador (en tránsito en `POST /login`, nunca persistida en
  claro — ya hasheada por ASP.NET Core Identity desde FEAT-001a). `Jwt:SigningKey` (secreto de
  infraestructura, no dato de usuario, pero su compromiso tiene el mismo impacto que una credencial
  raíz — ver TM-02).
- **PII**: mail del organizador (claim del JWT, ya clasificado y protegido desde FEAT-001a).

## Encryption (F-TM-07)

- En tránsito: HTTPS obligatorio (asunción de infraestructura, R-02 del PRD) cubre tanto el login
  como el envío de la cookie (`Secure` la fuerza explícitamente a nivel de cookie también).
- En reposo: la password ya está hasheada por Identity (sin cambios de este ticket). El JWT en sí
  no se persiste en ningún lado del lado del servidor (es stateless) ni del cliente más allá de la
  cookie del navegador — no aplica "en reposo" en el sentido de una base de datos.

## Mitigations folded into the spec

1. Block 1: transporte del JWT vía cookie `httpOnly`/`Secure`/`SameSite=Strict` en vez de body/
   `localStorage` (TM-03) — `AddJwtBearer` lee la cookie vía `JwtBearerEvents.OnMessageReceived`,
   CORS agrega `AllowCredentials()`.
2. Block 1: validación de longitud mínima de `Jwt:SigningKey` (32 bytes) al arrancar (TM-02).
3. Block 2: la cookie se fija en el controller (`Set-Cookie`), el body de la respuesta nunca
   incluye el JWT (`LoginResponse` sin campo `Token`).
4. Block 4: `AuthService` no persiste nada en `localStorage`; llama con `withCredentials: true`.

## Result

Risks: C:0 H:0 M:0 (2 aceptados: TM-01, TM-04) L:1 (TM-02, mitigado) — el HIGH original (TM-03) fue
eliminado por diseño, no solo mitigado. **PASSED.**
