# SAST — FEAT-009a (Confirmar compra, núcleo)

| Field | Value |
|-------|-------|
| Ticket | FEAT-009a |
| Date | 2026-08-21 |
| Scope | Cierre de fase CODE — los 3 bloques completos (Domain/Infrastructure, Application, Api) |
| Threat model | docs/daw/security/threat-FEAT-009a.md |

## Secrets

- ✅ F-SAST-01: sin API keys, passwords ni connection strings hardcodeados en los archivos
  modificados/creados de este ticket (grep de patrones `password =`, `secret =`,
  `connectionstring =` sobre Controllers, Application/Compradores, Application/Compras,
  Infrastructure/Compras, Infrastructure/Identity — sin resultados).
- ✅ `.env` presente en `.gitignore:19`.

## Injection

- ✅ F-SAST-02 (SQL): sin `FromSqlRaw`/`ExecuteSqlRaw` nuevos en este ticket. Las dos subqueries
  `NOT EXISTS (SELECT 1 FROM CompraCartones cc WHERE cc.CartonId = c.Id)` agregadas a
  `DescubrimientoRepository.cs:60,93` son texto SQL **completamente fijo**, sin ningún valor
  interpolado ni concatenado — no hay superficie de inyección porque no hay dato variable en la
  cláusula (confirmado leyendo el string literal). La misma exclusión en
  `BingoRepository.cs:62,79` usa LINQ (`_context.CompraCartones.Any(...)`), parametrizado por EF
  Core, no SQL crudo.
- ✅ F-SAST-02 (Redis/Lua): los dos scripts nuevos (`ScriptRevalidar`, `ScriptLiberar` en
  `CarritoRepository.cs`) reciben `sesionId`/`cartonId`/`cartonIds` EXCLUSIVAMENTE vía `ARGV`
  (`RedisValue[]` en la llamada `.NET`, nunca concatenados al string `@"..."` del script en tiempo
  de compilación). Las concatenaciones `..` DENTRO del script Lua (`CarritoRepository.cs:77,99`)
  operan sobre variables Lua ya pobladas desde `ARGV`/`HGETALL` en tiempo de ejecución de Redis, no
  sobre el texto del script — mismo patrón ya auditado para `ScriptIntentarAgregar` en FEAT-008b.
- ✅ F-SAST-03 (command injection): sin `exec`/`spawn`/`system` en ningún archivo de este ticket.
- ✅ F-SAST-05 (path traversal): ningún archivo de este ticket recibe input de usuario para
  construir una ruta de filesystem.

## XSS y funciones inseguras

- ✅ F-SAST-06: API REST pura, sin renderizado de HTML server-side en los archivos de este ticket.
- ✅ F-SAST-04/17: sin `eval()`/deserialización insegura. `JsonStringEnumConverter` (Program.cs) es
  la única adición de (de)serialización, estándar de `System.Text.Json`.
- ✅ F-SAST-08 (crypto débil): sin hashing manual de password — delegado íntegramente a ASP.NET
  Core Identity (`IdentityGateway.cs`), mismo patrón ya auditado para organizador en FEAT-001a. Sin
  MD5/SHA1/DES/ECB en ningún archivo de este ticket.

## Resto del checklist obligatorio

- ✅ F-SAST-07 (SSRF): sin llamadas salientes a URLs derivadas de input de usuario.
- ✅ F-SAST-09 (debug mode en producción): sin cambios a configuración de entorno/debug en este
  ticket.
- ✅ F-SAST-10 (logging de datos sensibles): `ExceptionHandlingMiddleware.cs` — todos los logs
  (líneas 87-90, 155-159, 175-178) registran únicamente `ex.GetType().Name` y
  `context.TraceIdentifier`, nunca CUIT/mail/teléfono/password. `CompradorService.cs`/
  `IdentityGateway.cs` no loguean el password en ningún punto (grep de `_logger` cruzado con
  `password|contraseña|cuit|token` sin resultados).
- ✅ F-SAST-11 (upload sin restricciones): no aplica, este ticket no agrega ningún endpoint de
  carga de archivos.
- ✅ F-SAST-12 (CSRF): cookie `bingocart_auth` con `SameSite=Strict` (mismo patrón ya auditado para
  `OrganizadoresController`), que es la mitigación CSRF estándar para cookies de auth en una API
  sin formularios cross-origin.
- ✅ F-SAST-14 (validación de input incompleta): `[FromBody] ConfirmarCompraRequest` solo expone
  `MedioPago` (confirmado por `daw-module-verifier`, sin campo `compradorId` en el DTO); el enum se
  valida vía `JsonStringEnumConverter` + `[Authorize]`; `RegistrarCompradorRequest`/
  `LoginCompradorRequest` heredan la validación de CUIT/password ya existente en el dominio
  (`Comprador.Crear`, `PasswordInvalidaException` delegada a Identity).
- ✅ F-SAST-15 (error handling que filtra internos): el catch genérico
  `ExceptionHandlingMiddleware.cs:150-166` NUNCA expone `ex.Message` ni stack trace — responde
  siempre con la constante fija `MensajeErrorInterno` (línea 31). `ex.Message` solo se usa en los
  catches de `DomainException` tipadas (líneas 97, 180), cuyos mensajes son texto de dominio
  diseñado para el usuario final, no detalles de infraestructura.

## R-01 (mitigación verificada, threat model)

- ✅ `compradorId` en `ComprasController.cs:49` se deriva EXCLUSIVAMENTE de
  `User.FindFirstValue(ClaimTypes.NameIdentifier)` — ningún parámetro de ruta, query ni body lo
  transporta. Verificado también por `daw-module-verifier` (traceability AC-01/NFR-02) y
  `daw-arch-auditor` (round 2) sobre el mismo archivo.

## Dependencias

- ✅ F-SAST-13/16: `dotnet list BingoCart.sln package --vulnerable --include-transitive` — sin
  paquetes vulnerables en ninguno de los 9 proyectos de la solución.

## Suppressions

Ninguna. 0 hallazgos Medium/High/Critical que requieran supresión documentada.

---

**Total: 20 checks clean, 0 vulnerabilidades (0 critical, 0 high, 0 medium)**
**Veredicto: PASSED**
