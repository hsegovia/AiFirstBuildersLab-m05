# Threat Model — FEAT-009b (Mail de confirmación de compra con PDF adjunto y reintentos)

| Field | Value |
|-------|-------|
| Ticket | FEAT-009b |
| Date | 2026-08-21 |
| Spec | docs/daw/specs/spec-FEAT-009b.md (a escribir tras este threat model) |
| PRD | docs/daw/prd/prd-FEAT-009b.md |

## Attack surfaces identified

1. `MailKitEmailSender` (Infrastructure) — primera llamada saliente del backend como CLIENTE (no
   servidor) hacia un relay SMTP externo. Primera dependencia de red saliente del proyecto.
2. `QuestPdfCartonRenderer` (Infrastructure) — primera generación de PDF del proyecto; datos de
   entrada mayormente system-generated (`Carton.Numeros`, `Carton.Id`), pero el cuerpo del mail
   incluye datos suministrados por el comprador (nombre/apellido, del registro de FEAT-009a).
3. `EnvioMailBackgroundService` (Infrastructure) — primer proceso en background del proyecto,
   corre sin autenticación, sin trigger externo, en un timer, y lee `EnviosMail`/datos de
   comprador de TODOS los compradores (no acotado a una sesión/request).
4. Configuración SMTP nueva (`Smtp:Host/Port/User/Password/From`) — nueva categoría de secreto para
   el proyecto.
5. Migración EF Core sobre la tabla `Compras`, ya en producción (`main`, FEAT-009a) — agrega
   `ConfirmacionId` NOT NULL sobre filas ya existentes.
6. Cuerpo del mail + PDF adjunto — primera vez que PII del comprador (nombre, apellido, mail) y
   datos de la compra (organizador, monto, números de cartón, GUID) salen del propio sistema hacia
   un tercero (el relay SMTP, y luego la bandeja de entrada del comprador).
7. Nuevo servicio `smtp4dev` en `docker-compose.yml` — solo dev/test, nunca debe ser alcanzable
   desde configuración de Producción.

## Trust boundaries

- **Backend → relay SMTP externo (NUEVO)**: el límite de confianza más importante que introduce
  este ticket. Una vez que el mail sale por SMTP, el sistema pierde control sobre esa copia de PII.
  Mitigación: TLS obligatorio en el tramo backend→relay (ver R-03), nunca loguear el cuerpo del
  mail ni el destinatario (ver R-02).
- **Backend → `EnviosMail`/SQL**: mismo nivel de confianza ya aceptado para el resto de la base
  (`Compras`, `AspNetUsers`) — sin límite nuevo. El `BackgroundService` lee entre compradores, pero
  no es una superficie atacable: no expone ningún endpoint HTTP ni trigger externo.
- **`docker-compose.yml` → `smtp4dev`**: límite dev/test-only. Nunca debe configurarse como el
  `Smtp:Host` de un ambiente de Producción — mismo patrón ya usado para separar secretos de
  desarrollo de secretos reales (`MSSQL_SA_PASSWORD`, `JWT_SIGNING_KEY`).

## Risks

🔴 **CRITICAL: ninguno.**

🟠 **HIGH**

- **R-01 (Information Disclosure — credenciales SMTP)**: si `MailKitEmailSender` logueara el texto
  crudo de una excepción de conexión/autenticación SMTP (`ex.ToString()`/`ex.Message`), podría
  filtrar detalles de la credencial o del servidor. **Mitigación:** los catches de
  `MailKitEmailSender`/`EnvioMailService.ProcesarPendientesAsync` loguean EXCLUSIVAMENTE
  `ex.GetType().Name` + `EnvioMailId`/`ConfirmacionId` — mismo patrón ya establecido en
  `ExceptionHandlingMiddleware` (nunca `ex.Message` para excepciones no tipadas). Credenciales
  viven solo en config (`appsettings`/variables de entorno de `docker-compose.yml`), nunca
  hardcodeadas ni committeadas. **Verificación obligatoria en CODE/SAST:** confirmar explícitamente
  que ningún log statement de este ticket incluye la password SMTP ni el cuerpo de una excepción de
  autenticación sin filtrar.
- **R-02 (Information Disclosure — PII del comprador en logs)**: `ProcesarPendientesAsync` compone
  el cuerpo del mail con nombre/apellido/mail del comprador y detalle de la compra. Si un log de
  fallo de envío incluyera ese contenido compuesto, se filtraría PII a los logs del sistema.
  **Mitigación:** todo log dentro de `EnvioMailService`/`MailKitEmailSender` usa únicamente
  identificadores opacos (`EnvioMailId`, `ConfirmacionId`, `CompradorId` como GUID) — nunca
  nombre/apellido/mail/cuerpo del mensaje. **Verificación obligatoria en CODE/SAST:** mismo
  criterio que F-SAST-10 (logging de datos sensibles), bloqueante si se encuentra una violación.

🟡 **MEDIUM**

- **R-03 (Tampering — mail en tránsito sin cifrar)**: la conexión SMTP backend→relay podría viajar
  en texto plano. **Mitigación:** `MailKitEmailSender` fuerza `SecureSocketOptions.StartTls` (o
  `SslOnConnect` según el puerto configurado) — nunca una conexión SMTP sin TLS en configuración de
  Producción. Cierra F-TM-07 (cifrado en tránsito para PII).
- **R-04 (Denial of Service — el BackgroundService muere ante una excepción no controlada)**: si
  `ProcesarPendientesAsync` lanzara una excepción no controlada dentro del loop del
  `BackgroundService`, el proceso completo podría detenerse permanentemente, deteniendo TODOS los
  reintentos futuros en silencio. **Mitigación:** try/catch por cada envío individual dentro del
  batch (una falla no aborta el resto) MÁS un try/catch alrededor de la iteración completa del
  `PeriodicTimer` (el servicio sobrevive y reintenta en el próximo tick, logueando la falla con los
  identificadores del R-02).
- **R-05 (Denial of Service — SMTP sin timeout)**: un relay SMTP no responsivo podría bloquear el
  hilo del `BackgroundService` indefinidamente, estancando los reintentos de TODOS los envíos
  pendientes. **Mitigación:** `MailKitEmailSender` configura explícitamente `SmtpClient.Timeout`
  (valor concreto a definir en el spec, ej. 30 segundos).
- **R-06 (Tampering/Integridad — backfill de `ConfirmacionId` en filas ya existentes)**: la
  migración agrega `ConfirmacionId` NOT NULL sobre una tabla `Compras` que ya tiene filas reales
  (FEAT-009a, en `main`). **Mitigación:** cada fila existente se backfillea con un
  `Guid.NewGuid()` propio (cada compra pre-existente se trata como su propio lote de confirmación
  de un solo elemento) — decisión de migración documentada explícitamente en el spec, no dejada
  implícita.

🟢 **LOW**

- **R-07 (Tampering — nombre/apellido del comprador sin escapar en el cuerpo HTML del mail)**: si
  el cuerpo del mail es HTML y el nombre/apellido (input del comprador, FEAT-009a) se concatenara
  como string crudo, podría alterar el renderizado del mail. Impacto real casi nulo (el único
  afectado sería el propio comprador viendo su propio mail), pero se documenta.
  **Mitigación:** construir el cuerpo vía la API de `BodyBuilder` de MimeKit (encoding seguro por
  default), nunca concatenación manual de HTML.
- **R-08 (`smtp4dev` alcanzable fuera de dev/test)**: el contenedor de test nunca debe ser el
  `Smtp:Host` de un ambiente real. **Mitigación:** mismo patrón de configuración por entorno ya
  usado para el resto de los secretos — sin necesidad de un mecanismo nuevo.

## Sensitive data classification (F-TM-05)

`Comprador` (nombre, apellido, mail) — mismo nivel de sensibilidad ya establecido en FEAT-009a,
pero este ticket es el PRIMERO en transmitirlo fuera de la base de datos/API propia, vía SMTP hacia
un relay externo y hacia la bandeja del propio comprador. Números de cartón + GUID — dato de
negocio de baja sensibilidad por sí solo, pero combinado con la identidad del comprador en el mismo
mensaje forma un registro de compra completo (mismo criterio ya aplicado a `Compra`). Credenciales
SMTP — mismo nivel de protección que `JWT_SIGNING_KEY`/`MSSQL_SA_PASSWORD` ya exigido en el
proyecto.

**Cifrado en tránsito (F-TM-07):** obligatorio TLS en la conexión SMTP (R-03). **Cifrado en
reposo:** `EnviosMail` no tiene mayor sensibilidad que `Compras`/`AspNetUsers`, ya protegidas por
los controles de acceso a la base existentes — sin requisito nuevo.

## Mitigations folded into the spec

1. Logs de `MailKitEmailSender`/`EnvioMailService` usan exclusivamente `ex.GetType().Name` +
   identificadores opacos — nunca `ex.Message` crudo, nunca PII del comprador ni cuerpo del mail
   (R-01, R-02).
2. `SecureSocketOptions.StartTls`/`SslOnConnect` obligatorio en `MailKitEmailSender` — nunca SMTP
   en texto plano en config de Producción (R-03).
3. Try/catch por envío individual + try/catch de iteración completa en
   `EnvioMailBackgroundService` — el servicio sobrevive a una falla no controlada (R-04).
4. `SmtpClient.Timeout` configurado explícitamente (R-05).
5. Migración: backfill de `ConfirmacionId` con un `Guid.NewGuid()` por fila existente, documentado
   explícitamente (R-06).
6. Cuerpo del mail construido vía `BodyBuilder` de MimeKit, nunca concatenación manual (R-07).
7. Config SMTP sigue el mismo patrón de secretos de entorno ya usado en `docker-compose.yml`
   (R-08).

Ningún riesgo CRITICAL/HIGH queda sin mitigación folded-in. R-01/R-02 (HIGH) tienen verificación
obligatoria explícita en CODE/SAST, mismo criterio que el threat model de FEAT-009a aplicó a su
propio R-01.

---

**Risks: C:0 H:2 (mitigados, verificación obligatoria) M:4 (mitigados) L:2 (mitigados)**
**Veredicto: PASSED**
