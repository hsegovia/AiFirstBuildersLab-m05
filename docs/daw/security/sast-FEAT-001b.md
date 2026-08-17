# SAST FEAT-001b: Login de organizador

| Field | Value |
|-------|-------|
| Ticket | FEAT-001b |
| Scope | diff (4 bloques: JWT/lockout, login, endpoint protegido, frontend) |
| Date | 2026-08-17 |
| Result | PASSED |

## Secrets (F-SAST-01)

✅ `Jwt:SigningKey` nunca en `appsettings.json` (raíz). El valor en
`appsettings.Development.json`/`docker-compose.yml` es una clave de desarrollo local documentada
explícitamente como tal (comentario en el propio archivo), sobreescribible por la variable de
entorno `JWT_SIGNING_KEY` — mismo patrón ya aceptado para `Tde:MasterKeyPassword` en FEAT-001a.
Validación de longitud mínima (32 bytes) agregada al arrancar (mitigación TM-02 del threat model).

## Injection (F-SAST-02/03)

✅ `IdentityGateway.AutenticarAsync` usa exclusivamente APIs parametrizadas de ASP.NET Core
Identity (`UserManager.FindByEmailAsync`, `SignInManager.CheckPasswordSignInAsync`), sin SQL crudo
ni concatenación. Sin `Process.Start`/ejecución de comandos en ningún archivo del diff.

## XSS y funciones inseguras (F-SAST-04/06/08/17)

✅ Sin `innerHTML`/`bypassSecurityTrust*` en `login-organizador.component.html` ni en ningún
archivo del feature `auth`. Sin `eval`/deserialización insegura. `JwtTokenService` firma con
HMAC-SHA256 (no MD5/SHA1/DES/ECB); `RequireSignedTokens` es `true` por default en
`TokenValidationParameters`, sin superficie de "alg: none".

## Resto de categorías obligatorias (F-SAST-07/09/10/11/12/14/15)

- SSRF (F-SAST-07) y debug mode (F-SAST-09): N/A, sin llamadas salientes nuevas ni cambios de
  configuración de entorno.
- Logging de datos sensibles (F-SAST-10): ✅ sin logging de password/token en ningún archivo del
  diff.
- Unrestricted upload (F-SAST-11): N/A.
- CSRF (F-SAST-12): ✅ mitigado por `SameSite=Strict` en la cookie `bingocart_auth`, evaluado y
  documentado en `docs/daw/security/threat-FEAT-001b.md` — el único endpoint protegido de este
  ticket (`GET /perfil`) es de solo lectura, sin efectos secundarios.
- Validación de input incompleta (F-SAST-14): ✅ `LoginOrganizadorRequest` valida `Mail`
  (`[Required, EmailAddress]`) y `Password` (`[Required]`).
- Manejo de errores que filtra internals (F-SAST-15): ✅ `CredencialesInvalidasException` tiene
  mensaje fijo genérico, sin distinguir mail inexistente/password incorrecta/cuenta bloqueada
  (AC-02 del PRD lo exige explícitamente).

## Dependencias (F-SAST-13/16)

✅ `dotnet list package --vulnerable --include-transitive` sobre `BingoCart.Api` → 0 paquetes
vulnerables, incluyendo `Microsoft.AspNetCore.Authentication.JwtBearer` 8.0.11 (único paquete
nuevo del ticket, versión alineada al resto del SDK .NET 8 del proyecto). Sin cambios en
`package.json`/`package-lock.json` del frontend.

## Suppressions

Ninguna — 0 findings Medium o superiores.

## Result

Total: 12 clean, 0 vulnerabilidades (0 Critical, 0 High, 0 Medium sin suprimir). **PASSED.**
