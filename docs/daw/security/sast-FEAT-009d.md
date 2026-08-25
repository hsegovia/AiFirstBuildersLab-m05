# SAST — FEAT-009d ("Mis cartones" del comprador y actualización de datos de cuenta)

| Field | Value |
|-------|-------|
| Ticket | FEAT-009d |
| Date | 2026-08-25 |
| Scope | Diff completo del ticket contra `main` (`git diff 6983312...HEAD`): 6 bloques, commits `e1e159d`, `57ea6ca`, `3ca1ee5`, `b7f9ebc`, `c4857f5`, `26697de` |
| Threat model | docs/daw/security/threat-FEAT-009d.md (R-01 resuelto ronda 2, R-02 a R-05 MEDIUM mitigados en spec, R-06 a R-08 LOW aceptados) |

## Secrets

- ✅ F-SAST-01: sin coincidencias de API keys/passwords/tokens/connection strings nuevos en los 43 archivos de producción del diff. `.env` sigue en `.gitignore`. Ningún `appsettings*.json` modificado en este ticket.

## Injection

- ✅ F-SAST-02 (SQL injection): sin `FromSqlRaw`/`FromSqlInterpolated`/`ExecuteSqlRaw` nuevos en el diff — los joins nuevos (`CompraRepository.ListarCartonesDelCompradorAsync`, `ObtenerCartonDelCompradorAsync`, `CompradorCuentaRepository.TieneSorteoInminenteAsync`) son 100% LINQ tipado con proyección, mismo patrón que `BingoRepository.ObtenerParaConfirmarCompraAsync`/`DirectorioRepository.ListarActivosAsync` ya existentes.
- N/A F-SAST-03/F-SAST-05 (command injection / path traversal): sin entrada de usuario alcanzando exec/spawn/rutas de archivo. El nombre de archivo del PDF (`{cartonId}.pdf`) se deriva del `Guid` de ruta, nunca de un string de usuario, y el PDF nunca toca el sistema de archivos (se genera en memoria y se devuelve directo — mitigación de R-03 del threat model, confirmada en código).

## XSS y funciones inseguras

- N/A F-SAST-06 (XSS): sin superficie HTML nueva — los 3 endpoints nuevos (`GET mis-cartones`, `GET mis-cartones/{id}/pdf`, `PUT mi-cuenta`) son JSON/binario, no renderizan HTML.
- ✅ F-SAST-04/F-SAST-17: sin `eval()`/deserialización insegura/`Reflection.Emit` en el diff.
- ✅ F-SAST-08 (crypto débil): la verificación de contraseña (`IdentityGateway.VerificarPasswordAsync`) usa `UserManager.CheckPasswordAsync`, el mismo primitivo de comparación de hash de Identity que ya usa `AutenticarAsync` — en ningún punto se lee ni se compara `PasswordHash` a mano, ni se usa MD5/SHA1/DES/ECB.

## Otras categorías obligatorias

- N/A F-SAST-07 (SSRF): sin llamadas salientes nuevas derivadas de input de usuario.
- N/A F-SAST-09 (debug mode): sin cambios de configuración de entorno.
- 🟠→✅ F-SAST-10 (logging de datos sensibles, HIGH) — **1 hallazgo real, corregido en este ticket** (ver sección dedicada abajo). Confirmado además, línea por línea, que el resto de los `_logger.Log*` nuevos/modificados de este ticket son seguros:
  - `CompradorService.cs:177-182` (log de auditoría, mitigación R-05 del threat model) — solo `compradorId`, timestamp UTC y **nombres** de campos modificados, nunca valores.
  - `ExceptionHandlingMiddleware.cs` (4 catches nuevos: `PlazoModificacionVencidoException`, `ContrasenaIncorrectaException`, `MailEnUsoException`, `CuitEnUsoException`) — todos pasan por `ManejarExcepcionDeDominioAsync`, que solo loguea `ex.GetType().Name` y `CorrelationId`, nunca `ex.Message` del lado del servidor (el mensaje de dominio, ya pensado para ser público, va únicamente al cuerpo de la respuesta HTTP).
  - La contraseña (`ContrasenaActual`) no aparece en ningún `_logger.Log*` del diff — confirmado con grep.
- N/A F-SAST-11 (upload sin restricciones): sin endpoints de upload en este ticket.
- ✅ F-SAST-12 (CSRF): los 3 endpoints nuevos son JSON/binario + JWT Bearer vía cookie `HttpOnly`/`Secure`/`SameSite=Strict`, mismo mecanismo ya vigente y ya evaluado en tickets anteriores (FEAT-008b/009a/c) — el `PUT /mi-cuenta` (mutación de credenciales) hereda la misma protección de `SameSite=Strict` que el resto de las mutaciones del proyecto, sin degradarla.
- ✅ F-SAST-14 (validación de input incompleta): `Page`/`PageSize` con `[Range]`; `cartonId` con constraint de ruta `:guid`; `ActualizarCuentaRequest` con `[Required]` en sus 5 campos (incluido `Cuit`, agregado en Block 6); CUIT validado con `CuitValidator` (longitud + dígito verificador, por separado). Verificado con tests para cada rama.
- ✅ F-SAST-15 (error handling que filtra internos): los 4 catches nuevos del middleware devuelven solo mensajes de dominio ya pensados para ser públicos; el catch genérico (`catch (Exception ex)`, línea 192) nunca devuelve `ex.Message` al cliente, solo un mensaje fijo (`"ErrorInterno"`).

## Hallazgo — F-SAST-10, HIGH, corregido

**Archivo:** `backend/BingoCart.Application/Compradores/CompradorService.cs:170-172` (antes del fix).

**Descripción:** `ActualizarCuentaAsync` interpolaba `resultado.Errores` (crudo, de
`IdentityGateway.ActualizarDatosAsync` → `UserManager.UpdateAsync`) directo en el mensaje de una
`InvalidOperationException`. Los errores de duplicado de Identity (`DuplicateUserName`, disparado
porque el proyecto no tiene `RequireUniqueEmail` y usa `UserName = mail`) **interpolan el valor
real** (`"UserName '{mail}' is already taken."`) — a diferencia de `PasswordInvalidaException`
(patrón ya aceptado desde FEAT-001a), cuyos errores describen reglas, nunca datos de usuario. Como
`InvalidOperationException` no es una excepción de dominio, cae en el catch genérico del middleware,
que pasa el objeto excepción completo a `_logger.LogError(ex, ...)` — la mayoría de los providers de
`ILogger` serializan `ex.Message` en la salida. El mail nunca llegaba a la respuesta HTTP (el
cliente recibe el mensaje genérico `"ErrorInterno"`), pero sí podía llegar a los **logs del
servidor**, violando el AC explícito del propio bloque (spec-FEAT-009d.md:684-685: "Ningún log de
este bloque incluye PII... ni `ex.Message`").

**Disparador:** condición de carrera real (dos actualizaciones de cuenta concurrentes apuntando al
mismo mail nuevo), sin necesidad de input malicioso — el pre-chequeo de colisión (pasos 4/5 del
método) no está protegido por ninguna transacción ni lock, así que la ventana entre el chequeo y la
escritura es explotable por comportamiento normal concurrente, no solo por un atacante.

**Triage:** `daw-sec-auditor`, verdict TRUE_POSITIVE, severidad HIGH confirmada (no reclasificable:
el catálogo no distingue por alcance de exposición ni probabilidad de disparo). No suprimible.

**Fix aplicado:** el mensaje de la excepción pasa a ser genérico y fijo
(`"No se pudo actualizar la cuenta del comprador."`), sin interpolar `resultado.Errores`. El detalle
diagnóstico se loguea aparte, con campos estructurados sin valores de usuario
(`compradorId`, `resultado.Errores.Count`), mismo patrón que el log de auditoría ya existente en el
mismo archivo.

**Test de regresión:** `CompradorServiceTests.ActualizarCuentaAsync_ConIdentityResultFallido_LanzaExcepcion`
extendido para simular un error de Identity con un mail interpolado
(`"UserName 'colision-race-condition@example.com' is already taken."`) y afirmar
`Assert.DoesNotContain(mailQueColisiono, excepcion.Message)`.

**Hallazgo sistémico (no corregido en este ticket, fuera de su alcance):** el catch genérico del
middleware asume que ninguna excepción no controlada llevará PII en `.Message` — asunción que este
hallazgo rompió una vez y podría romperse de nuevo con cualquier excepción futura no-de-dominio.
`daw-sec-auditor` recomienda una ADR o un ticket de deuda técnica para decidir entre sanitizar
centralmente en el middleware vs. mantener la disciplina caso por caso. Registrado acá para que
quede en el historial de decisiones de seguridad del proyecto, no como bloqueante de este ticket.

## IDOR / Pertenencia

- ✅ `MisCartonesController.cs` (listado y PDF) — `compradorId` derivado exclusivamente de
  `ClaimTypes.NameIdentifier`; el listado filtra por `Compras.CompradorId`; el PDF usa
  `CartonNoEncontradoException` con el **mismo mensaje** tanto si el cartón no existe como si es
  ajeno (patrón anti-enumeración de FEAT-009c, verificado con test de cuerpo idéntico byte a byte).
- ✅ `CompradoresController.PUT /mi-cuenta` — `compradorId` exclusivamente del claim; el body no
  lleva ningún identificador de usuario.

## Enumeración (R-04 del threat model, MEDIUM, mitigado)

- ✅ Verificación de contraseña **primera** de todas las validaciones de `ActualizarCuentaAsync`
  (antes de CUIT, antes de colisiones de mail/CUIT) — confirmado con test
  `ConContrasenaIncorrectaYMailYaEnUso_LanzaContrasenaIncorrectaException`: con contraseña incorrecta
  Y mail en colisión simultáneamente, se lanza `ContrasenaIncorrectaException`, nunca
  `MailEnUsoException`. `MailEnUsoException`/`CuitEnUsoException` no repiten el valor enviado en el
  mensaje.

## Rate limiting (NFR-02)

- ✅ Política `"comprador-cuenta"` (30 permits/5min, partición por `NameIdentifier`) aplicada a los
  3 endpoints nuevos (`MisCartonesController` ×2, `CompradoresController.PUT`) — única
  `AddPolicy("comprador-cuenta", ...)` en todo `Program.cs`, sin duplicación.

## Migración EF Core

- ✅ `20260823120414_AddNumeroCorrelativoACartones.cs`: `AddColumn` con backfill vía
  `migrationBuilder.Sql` que usa `ROW_NUMBER() OVER (PARTITION BY BingoId ORDER BY Id)` — sin
  concatenación de input externo, la columna/orden son fijos en el propio script de migración, no
  parametrizados desde fuera.

## Dependencias

- ✅ F-SAST-13/16: `dotnet list package --vulnerable --include-transitive` sobre los 9 proyectos de
  la solución → sin paquetes vulnerables. Ningún `.csproj` modificado en ninguno de los 6 bloques de
  este ticket (confirmado por `daw-arch-auditor` en cada bloque) — no hay superficie nueva de
  dependencias que auditar.

## Suppressions

Ninguna. El único hallazgo (F-SAST-10) se corrigió, no se suprimió — HIGH no es suprimible.

---

**Total: 1 vulnerabilidad encontrada y corregida (HIGH, F-SAST-10), 0 vulnerabilidades abiertas.
Suite completa (376 tests no-E2E) verde tras el fix, sin regresiones.**

**Veredicto: PASSED**
