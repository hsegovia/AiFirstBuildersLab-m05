# SAST — FEAT-009b (Mail de confirmación de compra con PDF adjunto y reintentos)

| Field | Value |
|-------|-------|
| Ticket | FEAT-009b |
| Date | 2026-08-21 |
| Scope | Diff completo de los 3 blocks (Domain, Application, Infrastructure) |
| Threat model | docs/daw/security/threat-FEAT-009b.md |

## Secrets

- ✅ **F-SAST-01**: `appsettings.json` (`Smtp` section) — `Host`/`User`/`Password`/`From` vacíos,
  `AllowInvalidCertificate: false`. `appsettings.Development.json` — valores dev-only apuntando al
  `smtp4dev` local (`localhost:2525`), nunca un servidor SMTP real, mismo patrón ya usado para
  `Redis:ConnectionString`. `docker-compose.yml` — `Smtp__Host=smtp4dev`, `Smtp__Port=25`,
  `Smtp__From=no-reply@bingocart.local`, `Smtp__AllowInvalidCertificate=true`; sin `Smtp__User`/
  `Smtp__Password` (smtp4dev no exige auth). Ningún secreto real committeado.

## Injection

- ✅ **F-SAST-02**: migración `20260821222859_AddEnviosMailYConfirmacionId.cs:28-29` —
  `migrationBuilder.Sql("UPDATE Compras SET ConfirmacionId = NEWID() WHERE ConfirmacionId IS
  NULL;")` es un string estático, sin interpolación ni input de usuario. `EnvioMailRepository.cs`
  usa exclusivamente LINQ-to-Entities vía `AppDbContext` — sin SQL crudo en este bloque.
- ✅ **F-SAST-05**: sin paths de archivo derivados de input de usuario (los PDFs se generan en
  memoria vía QuestPDF y se adjuntan como `byte[]`, nunca tocan el filesystem).

## XSS y funciones inseguras

- ✅ **F-SAST-06**: `EnvioMailMensaje.CuerpoHtml` se arma con `WebUtility.HtmlEncode` sobre los
  campos de usuario en `EnvioMailService.ArmarCuerpoHtml` (Application, Block 2) y
  `MailKitEmailSender.cs:38` lo asigna tal cual a `BodyBuilder.HtmlBody` — sin un segundo encode que
  lo corrompa, sin concatenación manual de HTML en ningún punto (mitigación R-07).
- ✅ **F-SAST-04/17**: sin `eval`, deserialización insegura, ni funciones de ejecución dinámica en
  ningún archivo de este ticket.
- ✅ **F-SAST-08**: sin uso de criptografía débil — `NEWID()`/`Guid.NewGuid()` para IDs, no para
  secretos ni contraseñas.

## Resto de categorías obligatorias

- ✅ **F-SAST-07 (SSRF)**: `Smtp:Host`/`Port` vienen de configuración (no de input de request), sin
  superficie SSRF nueva.
- ✅ **F-SAST-09 (debug mode)**: sin flags de debug nuevos en este ticket.
- ✅ **F-SAST-10 (logging de datos sensibles — R-01/R-02, HIGH, verificación obligatoria)**:
  auditados línea por línea los 4 puntos de log nuevos:
  - `MailKitEmailSender.cs:78-80` → únicamente `ex.GetType().Name`.
  - `EnvioMailBackgroundService.cs:67-69` → únicamente `ex.GetType().Name`.
  - `EnvioMailService.cs:62-65` (dato ausente) → únicamente `envio.Id`/`envio.ConfirmacionId`.
  - `EnvioMailService.cs:82-86` (falla de envío) → `envio.Id`/`envio.ConfirmacionId` +
    `ex.GetType().Name`.
  Ninguno interpola `ex.Message`, PII del comprador (nombre/apellido/mail) ni contenido del mensaje.
  `EnvioMailRepository.cs` no tiene ningún log statement.
  - ℹ️ **Informativo, no bloqueante**: `CompraService.cs:122-125` (catch de `EncolarAsync`) pasa el
    objeto `ex` completo a `_logger.LogWarning(ex, ...)`, lo que la mayoría de providers de logging
    renderiza con el texto completo de la excepción por debajo del mensaje. Ya evaluado en la
    revisión de Block 2 (`daw-module-verifier`): replica EXACTAMENTE el patrón pre-existente y ya
    aprobado del release de Redis unas líneas más abajo (mismo archivo), está fuera del scope
    explícito de R-01/R-02 (esas mitigaciones acotan el criterio a `MailKitEmailSender`/
    `EnvioMailService.ProcesarPendientesAsync`), y la excepción que puede llegar a este catch es de
    persistencia (`EncolarAsync` solo inserta una fila en `EnviosMail`, no toca SMTP ni arma el
    cuerpo del mail) — no puede contener credenciales SMTP ni PII del comprador por construcción del
    código que la lanza. No amerita FAIL ni suppression: es un patrón ya vigente en el proyecto,
    replicado a propósito por consistencia.
- ✅ **F-SAST-11 (upload sin restricción)**: sin endpoints de upload en este ticket.
- ✅ **F-SAST-12 (CSRF)**: sin endpoints HTTP nuevos — el envío de mail es un proceso interno sin
  superficie de API (confirmado en el spec, sección "API contract").
- ✅ **F-SAST-14 (validación de input incompleta)**: no aplica — `EnvioMail`/`EnvioMailMensaje` no
  reciben input directo de un request HTTP; los datos vienen de la propia base (`Compra` ya
  validada en FEAT-009a) o de configuración.
- ✅ **F-SAST-15 (errores que filtran internals)**: ningún mensaje de excepción se propaga a una
  respuesta HTTP — todo el manejo de errores de este ticket es interno (logs), sin superficie de
  API que devuelva detalles de una excepción al llamador.

## TLS / configuración SMTP (R-03, R-05, R-08)

- ✅ `MailKitEmailSender.cs:62` — `SecureSocketOptions.StartTls` hardcodeado (no
  `StartTlsWhenAvailable`): si el servidor no soporta TLS, `ConnectAsync` lanza en vez de degradar a
  texto plano. No es configurable a una opción más débil.
- ✅ `MailKitEmailSender.cs:16` — `TimeoutMs = 30_000` es una constante, no configurable a un valor
  mayor/deshabilitado.
- ✅ `Smtp:AllowInvalidCertificate` — verificado que StartTls se negocia SIEMPRE
  (`ConnectAsync(..., SecureSocketOptions.StartTls)` no depende de este flag); el flag únicamente
  controla si se instala `ServerCertificateValidationCallback` (que solo relaja la validación de la
  cadena de certificados, nunca desactiva el cifrado del canal). `false`/ausente en
  `appsettings.json` (Producción), `true` únicamente en `appsettings.Development.json` y en el env
  del servicio `api` de `docker-compose.yml` — ningún camino de configuración de Producción lo
  fija en `true`. R-03 permanece intacto.

## Dependencias

- ✅ **F-SAST-13/16**: `dotnet list BingoCart.Infrastructure/BingoCart.Infrastructure.csproj package
  --vulnerable --include-transitive` → "has no vulnerable packages given the current sources" — 0
  CVEs conocidos en MailKit 4.17.0, QuestPDF 2026.7.3, ni en ninguna dependencia transitiva.
  Ambos paquetes ya estaban pre-justificados a nivel de proyecto en la tabla Stack de `AGENTS.md`.

## Suppressions

Ninguna. No hay hallazgos Medium que requieran documentación de supresión.

---

**Total: 19 checks limpios, 0 vulnerabilidades (0 Critical, 0 High, 0 Medium). 1 nota informativa
(no bloqueante, patrón ya aprobado).**
**Veredicto: PASSED**
