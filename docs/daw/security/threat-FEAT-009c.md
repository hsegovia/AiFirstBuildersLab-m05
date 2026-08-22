# Threat Model — FEAT-009c (Confirmación y cancelación manual de pago)

| Field | Value |
|-------|-------|
| Ticket | FEAT-009c |
| Date | 2026-08-22 |
| Spec | docs/daw/specs/spec-FEAT-009c.md |
| PRD | docs/daw/prd/prd-FEAT-009c.md |

## Attack surfaces identified

1. `PATCH /api/compras/{id}/confirmar-pago`, `PATCH /api/compras/{id}/cancelar` — dos endpoints
   nuevos que mutan el estado de una `Compra` ajena al organizador que las invoca si no se valida
   ownership correctamente. Superficie IDOR clásica sobre el `{id}` de ruta.
2. `GET /api/compras/mias` — primer listado del organizador sobre `Compra` (entidad que hoy solo
   crea/lee el comprador o el propio sistema). Riesgo de exposición si accidentalmente incluyera
   datos del comprador.
3. `Compra.Estado` pasa de `private init` a `private set` — primera entidad de FEAT-009a/b cuyo
   estado deja de ser inmutable tras la creación. Introduce una ventana de condición de carrera
   entre dos requests concurrentes sobre la misma `Compra`.
4. Cancelar una compra cambia la disponibilidad de cartones ya vendidos en 4 sitios de consulta
   existentes (`BingoRepository.TieneComprasRegistradasAsync`/`ObtenerParaCarritoAsync`,
   `DescubrimientoRepository` x2) — un cambio de estado en una entidad tiene efecto en la
   superficie completa de descubrimiento/carrito.
5. `EnvioMail` (outbox de FEAT-009b, ya en producción) se extiende con un discriminador `TipoEnvio`
   y una segunda referencia nullable (`CompraId`) junto al `ConfirmacionId` ya existente (también
   vuelto nullable) — primer cambio de esquema sobre una tabla ya shippeada desde FEAT-009b.
6. Un nuevo tipo de mail (aviso de cancelación) atraviesa el mismo pipeline de outbox/reintentos/
   `BackgroundService` que el mail de confirmación, con un cuerpo distinto armado por
   `ArmarMensajeCancelacion` (nuevo).
7. Las 2 queries `FromSqlRaw` existentes de `DescubrimientoRepository.cs` ganan un placeholder
   parametrizado nuevo para el valor del enum `Cancelado`.

## Trust boundaries

- **Organizador autenticado → Api**: mismo límite ya establecido para `BingosController`/
  `ComprasController` — JWT validado, rol `Organizador` exigido, `organizadorId` derivado
  EXCLUSIVAMENTE del claim `NameIdentifier`, nunca de ruta/query/body. Este ticket no introduce un
  límite nuevo, reutiliza el ya existente.
- **Backend → relay SMTP**: sin cambios — el mail de cancelación reutiliza `MailKitEmailSender`
  (FEAT-009b), mismo límite ya mitigado (StartTls obligatorio, R-03 de threat-FEAT-009b.md).
- **Backend → SQL Server**: sin cambios de nivel de confianza — mismo límite ya aceptado.

## Risks

🔴 **CRITICAL: ninguno.**

🟠 **HIGH**

- **R-01 (Elevation of Privilege / IDOR — confirmar/cancelar compra ajena)**: si
  `CompraOrganizadorService` derivara `organizadorId` de cualquier fuente que no sea el claim JWT
  (o si `ObtenerCompraPropiaAsync` no comparara `compra.OrganizadorId` contra ese valor), un
  organizador podría confirmar o cancelar la compra de otro organizador solo conociendo su `Guid`.
  **Mitigación:** `organizadorId` EXCLUSIVAMENTE de `ClaimTypes.NameIdentifier` en los 3 endpoints
  nuevos (spec, Block 3, API contract); `ObtenerCompraPropiaAsync` (Block 2) lanza
  `CompraNoEncontradaException` (404, sin distinguir "no existe" de "es ajena") si
  `compra.OrganizadorId != organizadorId` — mismo patrón exacto que
  `BingoService.ObtenerBingoPropioSinComprasAsync`, ya auditado en FEAT-004/FEAT-007.
  **Verificación obligatoria en CODE/SAST:** confirmar que ningún endpoint nuevo acepta
  `organizadorId` desde `[FromBody]`/`[FromQuery]`/ruta.
- **R-02 (Information Disclosure — PII del comprador en el nuevo camino de cancelación)**:
  `EnvioMailRepository.ObtenerDatosParaCancelacionAsync` y `EnvioMailService.
  ArmarMensajeCancelacion` manejan mail/nombre/apellido del comprador — si algún log de este
  camino nuevo incluyera esos datos o el cuerpo del mensaje, se filtraría PII, exactamente el mismo
  riesgo que R-02 de threat-FEAT-009b.md, ahora aplicado a código nuevo. **Mitigación:** mismo
  criterio ya establecido — todo log dentro de este camino usa únicamente `EnvioMailId`/`CompraId`/
  `ex.GetType().Name`, nunca PII ni `ex.Message`. **Verificación obligatoria en CODE/SAST**, mismo
  criterio que F-SAST-10.

🟡 **MEDIUM**

- **R-03 (Tampering — nombre/apellido del comprador sin escapar en el mail de cancelación)**:
  `ArmarMensajeCancelacion` es código NUEVO — a diferencia del mail de confirmación (ya mitigado en
  FEAT-009b, R-07), este método todavía no existe, así que el mitigation debe declararse
  explícitamente para no perderlo en la implementación. **Mitigación:** `WebUtility.HtmlEncode` en
  `NombreComprador`/`NombreOrganizacion` antes de interpolarlos en el cuerpo HTML — ya plegado en el
  spec (Block 2, Logic) tras este threat model.
- **R-04 (Repudiation/Tampering — condición de carrera en `Compra.Estado` sin token de
  concurrencia)**: `Compra.Estado` pasa a mutable sin `RowVersion`/token optimista. Dos requests
  casi simultáneos sobre la misma `Compra` (ej. doble click en "confirmar" y "cancelar", o un
  retry de red) podrían pisarse silenciosamente — EF Core no detecta el conflicto sin un token
  explícito, así que "gana" el último `SaveChangesAsync` sin error. **Riesgo ACEPTADO** (F-TM-04):
  - **Quién lo acepta:** el usuario (hsegovia), vía confirmación explícita en PLAN (2026-08-22).
  - **Justificación:** la superficie de concurrencia real es baja — un único organizador actuando
    sobre su propia compra, sin más de un usuario administrando el mismo bingo hoy. No hay riesgo
    financiero (la conciliación de pago es siempre manual, fuera de esta transición — ningún cobro
    se duplica ni se pierde); el peor caso es un estado final ambiguo entre `Confirmado`/`Cancelado`
    tras un doble-click, recuperable manualmente por el organizador (puede volver a intentar la
    acción correcta, ya que ambos estados son terminales pero la UI mostraría el estado real tras
    refrescar).
  - **Condiciones de revisión:** re-evaluar si (a) el proyecto agrega administración multiusuario
    de un mismo bingo (varios operadores para un organizador), o (b) se observa evidencia real de
    conflictos (logs, reportes de usuarios) — en cualquiera de los dos casos, agregar un token de
    concurrencia optimista (`RowVersion`) a `Compra` en un ticket futuro.

🟢 **LOW**

- **R-05 (Injection — placeholder nuevo en `DescubrimientoRepository`'s `FromSqlRaw`)**: el valor
  de `EstadoCompra.Cancelado` debe viajar parametrizado (`{N}` de `FromSqlRaw`), nunca concatenado
  al string SQL — ya especificado explícitamente en el spec (Block 3, Logic), mismo criterio de
  "sin superficie de inyección" ya documentado en ese archivo desde FEAT-009a. **Verificación
  obligatoria en CODE/SAST:** confirmar que el valor se pasa como argumento posicional de
  `FromSqlRaw`, nunca interpolado/concatenado en el string.
- **R-06 (defensa en profundidad ya existente — sobre-disponibilidad accidental de un cartón)**: si
  alguna de las 4 queries modificadas tuviera un bug que mostrara un cartón de una compra NO
  cancelada como disponible, el índice `UNIQUE` de `CompraCartones.CartonId` (FEAT-009a, "defensa
  final" ya establecida) seguiría rechazando el doble-INSERT — `CompraRepository.CrearVariasAsync`
  ya traduce esa violación a `ReservaCarritoInvalidaException` sin persistir nada. No requiere
  mitigación nueva, es una capa ya vigente que este ticket no debilita.
- **R-07 (migración EF Core — backfill de `TipoEnvio`)**: el `DEFAULT 0` (`Confirmacion`) sobre
  todas las filas existentes de `EnviosMail` es correcto por construcción — ninguna fila
  `Cancelacion` existió antes de este ticket, así que no hay riesgo de misclasificación. Volver
  `ConfirmacionId` de NOT NULL a NULL es una relajación de constraint segura, sin pérdida de datos.

## Sensitive data classification (F-TM-05)

Mismo nivel de sensibilidad que threat-FEAT-009b.md — `Comprador` (nombre, apellido, mail) ahora
también fluye por un SEGUNDO camino estructuralmente distinto del outbox (tipo `Cancelacion`), con
el mismo destino final (relay SMTP externo). `Compra` (organizadorId, monto, estado) — dato de
negocio de sensibilidad baja-media, ya protegido por el control de ownership (R-01).

**Cifrado en tránsito (F-TM-07):** sin cambios — TLS obligatorio en la conexión SMTP ya mitigado en
FEAT-009b (R-03 de threat-FEAT-009b.md), reutilizado sin modificación por el camino de cancelación.
**Cifrado en reposo:** `EnviosMail`/`Compras` sin requisito nuevo, mismo nivel que el resto de la
base ya protegida por los controles de acceso existentes.

## Mitigations folded into the spec

1. `organizadorId` derivado exclusivamente del claim JWT en los 3 endpoints nuevos; ownership
   verificado vía `ObtenerCompraPropiaAsync` con el mismo patrón 404-sin-distinción ya establecido
   (R-01).
2. Logs del camino de cancelación (Application + Infrastructure) usan exclusivamente IDs opacos +
   `ex.GetType().Name`, nunca PII ni `ex.Message` (R-02).
3. `WebUtility.HtmlEncode` en `ArmarMensajeCancelacion` para `NombreComprador`/`NombreOrganizacion`
   — ya plegado en el spec (R-03).
4. Placeholder parametrizado (no concatenado) para el valor de `Cancelado` en las 2 queries
   `FromSqlRaw` de `DescubrimientoRepository.cs` (R-05).
5. R-04 (condición de carrera) queda como riesgo aceptado con los 3 campos de F-TM-04 documentados
   arriba — no requiere cambio de diseño en este ticket.

Ningún riesgo CRITICAL/HIGH queda sin mitigación folded-in. R-01/R-02 (HIGH) y R-05 (LOW, pero de
naturaleza de inyección) tienen verificación obligatoria explícita en CODE/SAST.

---

**Risks: C:0 H:2 (mitigados, verificación obligatoria) M:2 (1 mitigado, 1 aceptado con F-TM-04
completo) L:3 (mitigados/informativos)**
**Veredicto: PASSED**
