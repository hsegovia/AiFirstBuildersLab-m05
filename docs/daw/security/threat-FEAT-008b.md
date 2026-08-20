# Threat Model — FEAT-008b (Carrito de compras)

| Field | Value |
|-------|-------|
| Ticket | FEAT-008b |
| Date | 2026-08-20 |
| Spec | docs/daw/specs/spec-FEAT-008b.md |

## Attack surfaces identified

1. `POST /api/carrito/cartones/{cartonId}` — público, sin sesión previa requerida (la crea si no
   existe), primer endpoint del proyecto que escribe estado por sesión anónima.
2. `DELETE /api/carrito/cartones/{cartonId}` — público, idempotente.
3. `GET /api/carrito` — público, lee estado por sesión.
4. `POST /api/carrito/tandas/nueva` — público, recibe un body con `organizadorId?` y una lista de
   `Guid` (`cartonIdsDescartados`).
5. `CarritoRepository` (Infrastructure) — primer uso de Redis del proyecto: script Lua embebido
   (`EVAL`), claves construidas por interpolación de `sesionId`/`cartonId`.
6. Cookie `bingocart_carrito` — primer identificador de sesión del proyecto que NO es un JWT
   firmado (sin claims, sin firma, sin expiración explícita del lado del servidor más allá del TTL
   de las claves de Redis que referencia).
7. Servicio `redis` en `docker-compose.yml`, puerto expuesto al host (`16379:6379`, sin
   autenticación configurada — imagen `redis:7-alpine` por defecto).

## Trust boundaries

- **Cliente → Api**: el token de sesión (cookie `bingocart_carrito`) es generado por el servidor,
  nunca aceptado si viene con un valor arbitrario del cliente que no fue emitido por
  `ObtenerOCrearSesionId` — pero tampoco se **valida** contra ningún registro server-side de
  tokens emitidos (no existe tal registro): cualquier string que el cliente envíe como cookie se
  trata como un `sesionId` válido y se usa tal cual como sufijo de clave Redis. Es el límite de
  confianza más débil de este ticket — ver R-01.
- **Api → Infrastructure (Redis)**: `cartonId`/`organizadorId` llegan ya validados como `Guid` por
  el routing/model binding de ASP.NET Core antes de participar en cualquier clave o script Lua —
  nunca es un string arbitrario del usuario. `sesionId` (cookie) SÍ es un string arbitrario del
  cliente (ver arriba) que se interpola directamente en nombres de clave Redis
  (`carrito:{sesionId}`, `descartados:{sesionId}`).
- **Application → SQL Server**: `cartonId` se resuelve contra `IBingoRepository.
  ObtenerParaCarritoAsync` (LINQ, sin SQL crudo) antes de aceptar el agregado — un `cartonId` que
  no corresponde a un cartón real de un bingo activo nunca llega a reservarse en Redis.

## Risks

🔴 **CRITICAL: ninguno.**

🟠 **HIGH: ninguno.**

🟡 **MEDIUM**

- **R-01 (Spoofing — `sesionId` es un token no firmado, aceptado sin validación de origen):** un
  cliente puede enviar cualquier string como cookie `bingocart_carrito` y el sistema lo trata como
  un `sesionId` legítimo — no hay verificación de que ese valor fue efectivamente emitido por el
  servidor. **Mitigación:** el impacto de "adivinar" o fabricar el `sesionId` de otra persona es
  bajo por diseño — ese `sesionId` no es una credencial de autenticación (no protege dinero, no
  protege PII, no existe todavía ningún paso de compra en este ticket): en el peor caso, un
  atacante que adivine el `sesionId` de otra sesión puede ver/modificar el carrito ajeno (agregar/
  quitar cartones), una molestia, no una fuga de datos. La mitigación real está en la **entropía**
  del valor emitido por el servidor (`RandomNumberGenerator.GetBytes(32)`, 256 bits, CSPRNG — ver
  R-04) para que "adivinar" sea computacionalmente inviable; no se agrega firma HMAC porque no hay
  ningún claim que verificar más allá de "es la misma sesión que la vez anterior", y agregar firma
  no reduciría el riesgo de que el cliente reenvíe deliberadamente un valor ajeno si lo obtuvo por
  otro medio (ese es un problema de robo de cookie, no de falsificación de firma — mitigado por
  `HttpOnly`/`Secure`/`SameSite=Strict`, ya en el diseño).
- **R-02 (Tampering — interpolación de `sesionId` en nombres de clave Redis):** `CarritoRepository`
  arma claves como `carrito:{sesionId}` interpolando un string que viene del cliente (la cookie),
  no de un `Guid` validado por routing. **Mitigación:** Redis no tiene un lenguaje de "inyección"
  equivalente a SQL para nombres de clave — una clave es un string opaco para Redis, no hay forma de
  que un valor de `sesionId` "escape" hacia otro comando o clave no intencionada (a diferencia de
  SQL, donde concatenar cambia la estructura de la sentencia). El único riesgo real es de
  **colisión de namespace**: si un `sesionId` contuviera literalmente el string `:` seguido de algo
  que imite otro prefijo (ej. `"algo:reservado:carton:X"`), seguiría siendo una clave *distinta* de
  `reservado:carton:X` (Redis compara el string completo), así que no hay colisión posible con las
  claves de otro `cartonId`. CODE debe confirmar en SAST que el `sesionId` nunca se interpola en el
  **script Lua** de forma que cambie su lógica (solo se pasa como `ARGV`, nunca concatenado al
  cuerpo del script) — eso sí sería inyección real (Lua injection), distinto del namespacing de
  claves.

🟢 **LOW**

- **R-03 (Denial of Service — abuso de creación de carritos/reservas para agotar stock ajeno):** un
  atacante sin autenticación podría crear muchas sesiones (cada `POST` sin cookie previa genera una
  nueva) y reservar cartones sin intención de comprar, bloqueándolos por 5 minutos para
  participantes reales. **Mitigación:** NFR-02 (rate limiting 60 req/5min por IP, política
  `"carrito"`), mismo mecanismo ya validado en FEAT-005/008a. No se agrega ninguna mitigación
  adicional en este ticket (ej. CAPTCHA, límite de sesiones por IP) — mismo criterio de alcance ya
  aceptado en el threat model de FEAT-008a para el riesgo de scraping.
- **R-04 (predictibilidad del token de sesión si se usara un generador débil):** ya cubierto como
  parte de la mitigación de R-01 — **accepted risk explícito, no un riesgo nuevo**: se documenta acá
  solo para dejar constancia de que la elección de `RandomNumberGenerator` (CSPRNG, no `Guid.
  NewGuid()` ni `Random`) es deliberada y verificable en SAST, mismo nivel de cuidado que RNF-07 del
  PRD maestro exige para los números de un cartón.
- **R-05 (puerto de Redis expuesto sin autenticación, `docker-compose.yml`):** mismo patrón ya
  aceptado para SQL Server (`14330:1433`, threat-FEAT-001a.md, riesgo #2) — puerto expuesto al host
  únicamente para desarrollo local, nunca debe exponerse en un entorno compartido o productivo. Sin
  volumen persistente además (el carrito es efímero por diseño): un `docker-compose down` pierde
  todos los carritos activos, aceptable dado que no hay ninguna garantía de durabilidad prometida
  por el PRD para un carrito no confirmado.

## Sensitive data classification (F-TM-05)

`ItemCarrito`/`CarritoResponse` (`cartonId`, `nombreOrganizacion`, `nombreEvento`,
`precioUnitario`) — mismo nivel de dato público ya clasificado en FEAT-008a. El `sesionId` en sí
(cookie) no es PII ni credencial de autenticación — es un identificador de bucket temporal sin
ningún dato personal asociado en este ticket (el participante todavía no se registra, RF-14 en
adelante). F-TM-07 no aplica: nada de lo que este ticket persiste (en Redis, con TTL de minutos)
son datos personales ni credenciales.

## Mitigations folded into the spec

1. `RandomNumberGenerator.GetBytes(32)` (CSPRNG) para el token de sesión, no `Guid.NewGuid()` — ya
   en la spec, Block 3.
2. Cookie `HttpOnly`/`Secure`/`SameSite=Strict` — ya en la spec, Block 3, mismo patrón que
   `bingocart_auth` (FEAT-001b).
3. `cartonId`/`organizadorId` siempre `Guid` validados por routing antes de participar en cualquier
   clave o script Lua — ya en el diseño, Blocks 2-3.
4. Script Lua recibe `sesionId`/`cartonId` exclusivamente vía `ARGV`, nunca concatenados al cuerpo
   del script — a confirmar explícitamente en SAST (CODE), mismo criterio que
   `FromSqlInterpolated` en FEAT-008a.
5. Rate limiting `"carrito"` (60 req/5min/IP) en los 4 endpoints — ya en la spec, Block 3.
6. Redis sin volumen persistente, puerto expuesto solo para desarrollo local — ya en
   `docker-compose.yml` (Block 1).

Ningún riesgo CRITICAL/HIGH identificado. R-01/R-02 (MEDIUM) quedan mitigados por diseño: R-01 por
la baja sensibilidad del recurso protegido más la entropía CSPRNG del token; R-02 por cómo Redis
trata los nombres de clave como strings opacos, con el único vector real (Lua injection) a
confirmar explícitamente en SAST.

---

**Risks: C:0 H:0 M:2 (mitigados) L:3 (accepted)**
**Veredicto: PASSED**
