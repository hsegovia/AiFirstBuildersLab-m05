# SAST — FEAT-009c (Confirmación y cancelación manual de pago)

| Field | Value |
|-------|-------|
| Ticket | FEAT-009c |
| Date | 2026-08-22 |
| Scope | Diff completo del ticket: commits `a0c71d6` (Domain), `9c45d40` (Application), `70ebc72` (Infrastructure + Api) contra `main` |
| Threat model | docs/daw/security/threat-FEAT-009c.md (PASSED, C:0 H:2 M:2 L:3) |

## Secrets

- ✅ F-SAST-01: sin coincidencias de API keys/passwords/tokens/connection strings nuevos en el diff (`git diff main...HEAD` sin patrones sensibles fuera del placeholder de dev ya existente `Password=BingoCart_Dev2026!`, ya presente desde FEAT-009a/b).

## Injection

- ✅ F-SAST-02 (SQL injection): `DescubrimientoRepository.cs:70,105` — `(int)EstadoCompra.Cancelado` viaja como argumento posicional `{2}` de `FromSqlRaw` (`.FromSqlRaw(sql, cantidad, ahoraUtc, (int)EstadoCompra.Cancelado)` / `.FromSqlRaw(sql, cantidad, bingoId, (int)EstadoCompra.Cancelado)`), nunca concatenado/interpolado en el string SQL. Mitigación R-05 del threat model confirmada.
- ✅ Sin `FromSqlInterpolated` con datos de usuario, sin construcción de queries por concatenación de strings de entrada externa en ningún archivo del diff.
- N/A F-SAST-03/F-SAST-05 (command injection / path traversal): sin entrada de usuario alcanzando exec/spawn/rutas de archivo en este diff.

## XSS y funciones inseguras

- ✅ F-SAST-06 (XSS): `EnvioMailService.cs:186-187` (`ArmarMensajeCancelacion`) — `WebUtility.HtmlEncode` aplicado a `datos.NombreComprador` Y `datos.NombreOrganizacion` antes de interpolar en el cuerpo HTML. Mitigación R-03 del threat model confirmada.
- ✅ F-SAST-04/F-SAST-17: sin `eval()`/deserialización insegura en el diff.
- ✅ F-SAST-08 (crypto débil): sin cambios de criptografía en este ticket.

## Otras categorías obligatorias

- N/A F-SAST-07 (SSRF): sin nuevas llamadas salientes a URLs derivadas de input de usuario.
- N/A F-SAST-09 (debug mode): sin cambios de configuración de entorno.
- ✅ F-SAST-10 (logging de datos sensibles — HIGH, mandatory-verify por threat model R-02): verificado línea por línea en los 4 `_logger.LogWarning` nuevos/modificados de este ticket:
  - `CompraOrganizadorService.cs:65-68` — solo `compra.Id`.
  - `EnvioMailService.cs:78-81` (rama Confirmacion, sin cambios de comportamiento) — solo `envio.Id`/`envio.ConfirmacionId`.
  - `EnvioMailService.cs:92-95` (rama Cancelacion, nueva) — solo `envio.Id`/`envio.CompraId`.
  - `EnvioMailService.cs:114-118` (catch genérico de `ProcesarPendientesAsync`) — `envio.Id`, `envio.TipoEnvio` (enum), `ex.GetType().Name`. Ninguno incluye `ex.Message` ni PII del comprador (nombre/apellido/mail).
- N/A F-SAST-11 (upload sin restricciones): sin endpoints de upload en este ticket.
- N/A F-SAST-12 (CSRF): sin cambios en la política CSRF existente; los 3 endpoints nuevos son JSON+JWT Bearer, mismo criterio ya vigente en el proyecto.
- ✅ F-SAST-14 (validación de input incompleta): `{id}` de ruta tipado `Guid` (400 automático si no parsea, binding de ASP.NET Core); `ListarComprasQuery` mirrorea `ListarBingosQuery` (rangos/atributos ya validados).
- ✅ F-SAST-15 (error handling que filtra internos): los 2 catches nuevos de `ExceptionHandlingMiddleware.cs` (409 `CompraEstadoInvalidoException`/404 `CompraNoEncontradaException`) devuelven solo el mensaje de dominio ya pensado para ser público, mismo shape que sus hermanos existentes — sin stack traces ni detalles de infraestructura en la respuesta HTTP.

## IDOR (R-01 del threat model, HIGH, mandatory-verify)

- ✅ `ComprasController.cs:89,113,134` — `organizadorId` derivado EXCLUSIVAMENTE de `Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!)` en los 3 endpoints nuevos (`ConfirmarPago`, `Cancelar`, `ListarMias`). Ningún `[FromBody]`/`[FromQuery]`/parámetro de ruta provee `organizadorId`. `ObtenerCompraPropiaAsync` (Application) hace el chequeo de ownership y devuelve 404 sin distinguir "no existe" de "es ajena" (FR-08).

## Riesgo aceptado (R-04, MEDIUM)

- ✅ Confirmado que sigue documentado como riesgo ACEPTADO en `threat-FEAT-009c.md` con los 3 campos obligatorios de F-TM-04 (quién lo acepta: hsegovia, 2026-08-22; justificación: baja concurrencia real, sin riesgo financiero; condiciones de revisión: administración multiusuario o evidencia real de conflictos). No requiere fix en este ticket — no es una vulnerabilidad sin atender, es una decisión de riesgo ya tomada y documentada por el usuario.

## Migración EF Core

- ✅ `20260822150224_AddTipoEnvioYCompraIdAEnviosMail.cs`: sin `migrationBuilder.Sql` con texto crudo — el backfill de `TipoEnvio` DEFAULT 0 se hace vía `AddColumn` con `defaultValue`, no vía SQL concatenado.

## Dependencias

- ✅ F-SAST-13/16: `dotnet list BingoCart.Infrastructure package --vulnerable --include-transitive` → sin paquetes vulnerables. Ningún `.csproj` modificado en este ticket (confirmado por `daw-arch-auditor` en Block 3) — no hay superficie nueva de dependencias que auditar.

## Suppressions

Ninguna. No hay hallazgos Medium sin mitigar en este ticket.

---

**Total: 0 vulnerabilidades (0 Critical, 0 High, 0 Medium sin mitigar), 3 mitigaciones de threat model confirmadas en código (R-01 IDOR, R-02 logging, R-03 HTML encoding), 1 riesgo aceptado confirmado como tal (R-04).**

**Veredicto: PASSED**
