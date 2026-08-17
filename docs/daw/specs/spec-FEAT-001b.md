# Spec FEAT-001b: Login de organizador

| Field | Value |
|-------|-------|
| Ticket | FEAT-001b |
| PRD | docs/daw/prd/prd-FEAT-001b.md |
| Tier | FEATURE |
| Date | 2026-08-17 |
| Spec loops | 0 |

## Summary

Implementa autenticación de organizador con mail y contraseña, emitiendo un JWT firmado con HMAC
(expiración 60 min) y aplicando lockout de cuenta (5 intentos fallidos → bloqueo de 5 min) vía
`SignInManager<ApplicationUser>`. Sigue exactamente el patrón de capas de FEAT-001a (Controller →
Application Service → `IIdentityGateway` → Infrastructure). Se agrega un endpoint mínimo protegido
(`GET /api/organizadores/perfil`) cuyo único propósito es hacer verificable end-to-end el rechazo
de JWT expirados (AC-04), ya que ningún endpoint de negocio protegido existe todavía — decisión
tomada con el usuario en PLAN.

**El JWT se transporta en una cookie `httpOnly`, nunca en el body de la respuesta ni en
`localStorage`** — decisión tomada con el usuario en `/daw-threat-modeling` (PLAN), para eliminar
el vector de robo del token vía una futura vulnerabilidad XSS en el frontend. Esto implica: el
backend fija la cookie con `Set-Cookie` (Block 2), el pipeline de `AddJwtBearer` la lee del request
en vez de un header `Authorization` (Block 1), CORS necesita `AllowCredentials()` (Block 1) y el
frontend llama con `withCredentials: true` sin persistir el token en ningún lado (Block 4). Ver
`docs/daw/security/threat-FEAT-001b.md` para el análisis completo.

## Coverage: PRD → blocks

| Requirement | Covered by |
|---|---|
| FR-01 | Block 2 |
| FR-02 | Block 1, Block 2, Block 3 |
| FR-03 | Block 1, Block 2 |
| NFR-01 | Strategy: Block 1 configura `TokenValidationParameters.ClockSkew = TimeSpan.Zero` (sin margen de tolerancia) y Block 3 verifica el rechazo exacto tras expirar, sin depender de esperar 60 minutos reales en el test (ver Block 3, `Required tests`). |
| NFR-02 | Strategy: Block 2 — la emisión del JWT es sincrónica en memoria (firma HMAC-SHA256), sin llamadas externas más allá de la verificación de password contra Identity (ya indexado por mail único); el login completo (verificación + emisión) se mide en el test de integración del bloque. |

## Dependencies between blocks

Block 1 (infraestructura JWT + lockout) no depende de nada — es la base. Block 2 (lógica de login)
depende de Block 1 (usa el servicio de tokens y la política de lockout). Block 3 (endpoint
protegido) depende de Block 1 (pipeline de autenticación, y del `TestTimeProvider` para el caso
negativo) y de Block 2 (necesita poder loguearse para obtener un token real en sus tests). Block 4
(frontend) depende de Block 2 (consume el endpoint de login).
Orden: 1 → 2 → 3 → 4, sin paralelismo posible (cada bloque consume artefactos del anterior).

**Decisión de PLAN (no reabrir en CODE):** el endpoint `GET /api/organizadores/perfil` (Block 3) es
un endpoint mínimo de verificación, no una funcionalidad de negocio — su único campo de respuesta es
el mail del organizador autenticado, tomado del claim del JWT. No implementa nada del dashboard ni
de gestión de bingos (RF-02 en adelante), que siguen fuera de alcance de este ticket.

## Block 1 — Infraestructura: emisión/validación de JWT + política de lockout de Identity

**Files**
- `backend/BingoCart.Application/Auth/IJwtTokenService.cs` (new) — puerto: `string GenerarToken(Guid
  organizadorId, string mail)`, sin exponer detalles de implementación (algoritmo, clave) a la capa
  de Application.
- `backend/BingoCart.Infrastructure/Auth/JwtTokenService.cs` (new) — implementa `IJwtTokenService`
  usando `System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler`, `SigningCredentials` HMAC-SHA256
  con la clave leída de `IOptions<JwtSettings>`. Claims: `NameIdentifier` (organizadorId),
  `Email` (mail). Constructor recibe `TimeProvider` inyectado (además de `IOptions<JwtSettings>`) —
  expiración = `_timeProvider.GetUtcNow().UtcDateTime + JwtSettings.ExpirationMinutes`. Una sola
  firma pública, `GenerarToken(Guid organizadorId, string mail)`: en producción resuelve
  `TimeProvider.System` vía DI; en tests, se inyecta un `TestTimeProvider : TimeProvider` (doble de
  test que sobreescribe `GetUtcNow()`, definido en el proyecto de test, sin paquete adicional) para
  construir un `JwtTokenService` cuyo reloj ya está en el pasado y así emitir un token
  "ya vencido" sin esperar 60 minutos reales — evita el overload `internal`/`InternalsVisibleTo`
  (decisión de PLAN tras revisión de `daw-arch-auditor`: sin precedente de `InternalsVisibleTo` en
  el repo, `TimeProvider` es la alternativa idiomática de .NET 8 y mantiene el contrato público
  mínimo).
- `backend/BingoCart.Infrastructure/Auth/JwtSettings.cs` (new) — `record JwtSettings { public
  required string Issuer { get; init; } public required string Audience { get; init; } public
  required string SigningKey { get; init; } public int ExpirationMinutes { get; init; } = 60; }`
- `backend/BingoCart.Api/Program.cs` (modified, también) — agrega
  `builder.Services.AddSingleton(TimeProvider.System);` junto al registro de `IJwtTokenService`
  (ver más abajo).
- `backend/BingoCart.Infrastructure/BingoCart.Infrastructure.csproj` (modified) — agrega el paquete
  NuGet `Microsoft.AspNetCore.Authentication.JwtBearer` (versión alineada al SDK .NET 8 ya usado en
  el proyecto — justificado por FR-02, único paquete nuevo de este ticket).
- `backend/BingoCart.Infrastructure/Identity/IdentityConfiguration.cs` (modified) — agrega, en la
  misma cadena de extensión sobre `IdentityBuilder` que ya configura `Password`:
  `options.Lockout.MaxFailedAccessAttempts = 5; options.Lockout.DefaultLockoutTimeSpan =
  TimeSpan.FromMinutes(5); options.Lockout.AllowedForNewUsers = true;`
- `backend/BingoCart.Api/Program.cs` (modified) — agrega, después del registro de Identity:
  `builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
  builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
  builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
  { /* TokenValidationParameters con Issuer/Audience/SigningKey desde JwtSettings, ClockSkew =
  TimeSpan.Zero */ options.Events = new JwtBearerEvents { OnMessageReceived = context => {
  if (context.Request.Cookies.TryGetValue("bingocart_auth", out var token)) context.Token = token;
  return Task.CompletedTask; } }; });`. El token viaja en la cookie `bingocart_auth` (fijada por
  Block 2), NO en el header `Authorization` — por eso `OnMessageReceived` lo extrae de
  `Request.Cookies` explícitamente; sin este evento, `AddJwtBearer` solo mira el header
  `Authorization` por defecto y `[Authorize]` (Block 3) rechazaría siempre, incluso con la cookie
  presente. Agrega `app.UseAuthentication();` **antes** de `app.UseAuthorization();` (línea 92
  actual) — el orden importa: sin `UseAuthentication` primero, `[Authorize]` del Block 3 siempre
  rechazaría con 401 genérico sin siquiera intentar validar el token.
- `backend/BingoCart.Api/Program.cs` (modified, mismo archivo) — en la política CORS existente
  (`options.AddPolicy("frontend", ...)`, línea 69 actual), agrega `.AllowCredentials()` a la cadena
  — necesario para que el navegador envíe/reciba la cookie `bingocart_auth` en requests
  cross-origin (`:8000` → `:8080`). Ya es compatible: la política usa `WithOrigins("http://
  localhost:8000")` explícito, no `AllowAnyOrigin()` (que es incompatible con
  `AllowCredentials()` por especificación de CORS).
- `backend/BingoCart.Api/appsettings.json` (modified) — agrega sección:
  ```json
  "Jwt": { "Issuer": "BingoCart", "Audience": "BingoCart", "ExpirationMinutes": 60 }
  ```
  (sin `SigningKey` — se inyecta exclusivamente por variable de entorno, nunca en el archivo
  versionado, mismo patrón que `Tde:MasterKeyPassword`).
- `backend/BingoCart.Api/appsettings.Development.json` (modified) — agrega `"Jwt": { "SigningKey":
  "<clave de desarrollo local, ≥32 bytes, documentada como no apta para producción>" }` — mismo
  criterio que la password de SQL Server ya presente en este archivo (credencial de desarrollo, no
  secreto real).
- `docker-compose.yml` (modified) — servicio `api`, agrega variable de entorno
  `Jwt__SigningKey=${JWT_SIGNING_KEY:-<mismo default de desarrollo que appsettings.Development.json>}`,
  mismo patrón que `ConnectionStrings__Default`/`Tde__MasterKeyPassword` (líneas 52,55 actuales).

**Logic**
`JwtTokenService` es la única clase que conoce el algoritmo de firma y la clave — el resto del
sistema solo ve `IJwtTokenService.GenerarToken(id, mail) -> string`. La política de lockout se
configura una vez a nivel de `IdentityOptions` y la aplica automáticamente
`SignInManager.CheckPasswordSignInAsync` (usado en Block 2) sin lógica adicional en Application ni
Infrastructure — es responsabilidad exclusiva de ASP.NET Core Identity.

**API contract**
N/A — este bloque no expone ningún endpoint, es infraestructura consumida por Block 2 y Block 3.

**Data model**
N/A — no agrega ni modifica ninguna entidad persistida. `JwtSettings` es un objeto de configuración
en memoria, no una entidad de dominio.

**Input validation**
N/A — no recibe input de usuario directamente (lo recibe Block 2, que valida antes de invocar este
servicio).

**Error handling**
- Si `JwtSettings.SigningKey` no está configurada (variable de entorno ausente) → el proceso debe
  fallar al arrancar (`IOptions<JwtSettings>` con `required` en el record lanza en el binding), no
  arrancar en un estado donde emitiría tokens sin firma válida o lanzaría una excepción no
  controlada en el primer login real.
- Si `JwtSettings.SigningKey` está presente pero tiene menos de 32 bytes (256 bits, mínimo
  recomendado para HMAC-SHA256) → el proceso debe fallar al arrancar con un mensaje explícito
  (`IValidateOptions<JwtSettings>` o una validación manual en el registro de `Configure<JwtSettings>`
  con `.Validate(s => Encoding.UTF8.GetBytes(s.SigningKey).Length >= 32, "Jwt:SigningKey debe tener
  al menos 32 bytes")`). Mitigación agregada en `/daw-threat-modeling` (PLAN): una clave corta
  haría la firma HMAC forzable por fuerza bruta, permitiendo forjar tokens para cualquier
  organizador sin conocer su password.

**Required tests**
- [ ] `JwtTokenService.GenerarToken` (con `TimeProvider.System`) produce un token que
  `JwtSecurityTokenHandler.ValidateToken` acepta con los mismos `Issuer`/`Audience`/`SigningKey`
  configurados, con los claims `NameIdentifier`/`Email` esperados — valida NFR-01 (emisión
  correcta).
- [ ] `JwtTokenService.GenerarToken` construido con un `TestTimeProvider` cuyo `GetUtcNow()` ya está
  61 minutos en el pasado produce un token que `ValidateToken` rechaza con
  `SecurityTokenExpiredException` cuando `ClockSkew = TimeSpan.Zero` — valida NFR-01 (sin
  tolerancia).
- [ ] La app arranca correctamente con `Jwt:SigningKey` presente en configuración (test de
  integración liviano, o cubierto implícitamente por los tests de Block 3 que levantan
  `WebApplicationFactory`).
- [ ] Un `IConfiguration` construido sin la clave `Jwt:SigningKey` hace fallar el binding de
  `IOptions<JwtSettings>` (excepción de binding por el `required` del record) — valida el error
  documentado arriba (Error handling): el proceso no debe arrancar en un estado sin firma válida.
- [ ] Un `IConfiguration` con `Jwt:SigningKey` de menos de 32 bytes hace fallar la validación de
  opciones al resolver `IOptions<JwtSettings>.Value` — valida la mitigación de clave débil.

**Completion criterion**
`JwtTokenService` emite y las opciones de `AddJwtBearer` validan tokens con los mismos parámetros;
un token con expiración pasada es rechazado por `ValidateToken` sin tolerancia de reloj. Los 4
proyectos + `BingoCart.Api` compilan con el paquete `JwtBearer` agregado.

## Block 2 — Lógica de login (Application + Infrastructure) y endpoint

**Files**
- `backend/BingoCart.Application/Organizadores/Dtos/LoginOrganizadorRequest.cs` (new) — `record
  LoginOrganizadorRequest([Required, EmailAddress] string Mail, [Required] string Password);` —
  DataAnnotations en los parámetros del constructor primario (NO con `[property: ...]`), mismo
  patrón exacto que `RegistrarOrganizadorRequest.cs` (el model binder de ASP.NET solo lee la
  metadata puesta ahí).
- `backend/BingoCart.Application/Organizadores/Dtos/LoginOrganizadorResponse.cs` (new) — `record
  LoginOrganizadorResponse(string Token, DateTime ExpiraEnUtc);` — sin exponer la entidad
  `Organizador` ni `ApplicationUser` directamente. **Uso exclusivamente interno**: `Token` viaja de
  Application al controller para que este arme la cookie httpOnly (ver más abajo); el controller
  NUNCA devuelve este `Token` en el body de la respuesta HTTP — para eso existe
  `LoginResponse` (`Api/Contracts/`, ver más abajo), que no tiene el campo `Token`.
- `backend/BingoCart.Api/Contracts/LoginResponse.cs` (new) — `record LoginResponse(DateTime
  ExpiraEnUtc);` — el contrato público de `POST /login`, sin el JWT (que va en la cookie, no en el
  body — decisión de `/daw-threat-modeling` en PLAN). Mismo criterio de ubicación que
  `PerfilOrganizadorResponse` (Block 3): vive en `Api/Contracts/` porque es un contrato de
  transporte, no algo que Application construya.
- `backend/BingoCart.Application/Organizadores/IIdentityGateway.cs` (modified) — agrega el método
  `Task<ResultadoAutenticacion> AutenticarAsync(string mail, string password);` donde
  `ResultadoAutenticacion` es un `record ResultadoAutenticacion(EstadoAutenticacion Estado, Guid?
  OrganizadorId)` nuevo (archivo `ResultadoAutenticacion.cs` en la misma carpeta), con
  `EstadoAutenticacion` como enum (`Exitoso`, `CredencialesInvalidas`, `CuentaBloqueada`) en el mismo
  archivo. `OrganizadorId` va poblado solo cuando `Estado == Exitoso` (con el `Guid` de
  `ApplicationUser.Id`, que por diseño de FEAT-001a es el mismo id que `Organizador.Id` — no hay
  ninguna búsqueda adicional que hacer, es el id del `ApplicationUser` ya recuperado por mail). Esto
  reemplaza la ambigüedad detectada en `daw-arch-auditor` (PLAN): sin este campo, `OrganizadorService`
  no tenía ninguna forma especificada de obtener el id a pasarle a `IJwtTokenService.GenerarToken`.
- `backend/BingoCart.Infrastructure/Identity/IdentityGateway.cs` (modified) — implementa
  `AutenticarAsync`: `UserManager.FindByEmailAsync(mail)` → si `null`, devuelve
  `new ResultadoAutenticacion(CredencialesInvalidas, null)` sin tocar ningún contador (no existe
  cuenta que incrementar). Si existe,
  `SignInManager<ApplicationUser>.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true)`
  → `Succeeded` mapea a `new ResultadoAutenticacion(Exitoso, user.Id)`, `IsLockedOut` a
  `new ResultadoAutenticacion(CuentaBloqueada, null)`, cualquier otro resultado (`IsNotAllowed`,
  fallo simple) a `new ResultadoAutenticacion(CredencialesInvalidas, null)`. `SignInManager` se
  inyecta en el constructor junto al `UserManager` ya existente.
- `backend/BingoCart.Application/Organizadores/IOrganizadorService.cs` (modified) — agrega
  `Task<LoginOrganizadorResponse> AutenticarAsync(LoginOrganizadorRequest request);`
- `backend/BingoCart.Application/Organizadores/OrganizadorService.cs` (modified) — implementa
  `AutenticarAsync`: llama a `IIdentityGateway.AutenticarAsync(request.Mail, request.Password)`; si
  `resultado.Estado == Exitoso`, llama a
  `IJwtTokenService.GenerarToken(resultado.OrganizadorId!.Value, request.Mail)` y arma la respuesta
  con `ExpiraEnUtc = _timeProvider.GetUtcNow().UtcDateTime.AddMinutes(_jwtSettings.ExpirationMinutes)`
  (mismo `TimeProvider`/`JwtSettings` inyectados que usa `JwtTokenService`, para que el valor
  devuelto al cliente coincida exactamente con la expiración real del token). Si
  `CredencialesInvalidas` o `CuentaBloqueada`, lanza una excepción de dominio nueva
  `CredencialesInvalidasException` (ambos casos con el MISMO tipo de excepción y MISMO mensaje —
  decisión de diseño explícita, ver Error handling) capturada por `ExceptionHandlingMiddleware`
  existente.
- `backend/BingoCart.Domain/Auth/Exceptions/CredencialesInvalidasException.cs` (new) — hereda de
  `DomainException` (mismo patrón que las 4 excepciones ya existentes de `RegistrarAsync`), mensaje
  genérico: `"Credenciales inválidas."` — sin distinguir mail inexistente, password incorrecta o
  cuenta bloqueada, para no filtrar por ningún canal si una cuenta existe (AC-02 aplicado también al
  caso de lockout, más estricto que lo mínimo que pide el PRD). Carpeta `Domain/Auth/` (no
  `Domain/Organizadores/`) porque "credenciales inválidas" es un resultado de autenticación, no un
  invariante del agregado `Organizador` — espeja la separación que Application ya hace entre
  `Application/Auth/` (JWT) y `Application/Organizadores/` (registro/perfil), corrigiendo la
  inconsistencia que `daw-arch-auditor` señaló en PLAN.
- `backend/BingoCart.Api/Controllers/OrganizadoresController.cs` (modified) — agrega `[HttpPost(
  "login")] public async Task<ActionResult<LoginResponse>> Login([FromBody]
  LoginOrganizadorRequest request)`: llama a `IOrganizadorService.AutenticarAsync(request)`, y con
  el `LoginOrganizadorResponse` resultante arma la cookie —
  `Response.Cookies.Append("bingocart_auth", resultado.Token, new CookieOptions { HttpOnly = true,
  Secure = true, SameSite = SameSiteMode.Strict, Expires = resultado.ExpiraEnUtc, Path = "/" })` —
  y devuelve `Ok(new LoginResponse(resultado.ExpiraEnUtc))` (sin el token en el body). Es la ÚNICA
  lógica que vive en el controller en vez de en Application (fijar la cookie es un detalle de
  transporte HTTP, no de negocio — mismo criterio que ya aplica Block 3 para justificar su
  excepción al patrón de capas).
- `backend/BingoCart.Api/Middleware/ExceptionHandlingMiddleware.cs` (modified) — agrega el mapeo
  `CredencialesInvalidasException -> 401 Unauthorized` a la tabla de excepciones tipadas ya
  existente (mismo mecanismo que usan las 4 excepciones de registro, sin catch silencioso).

**API contract**
- Method + path: `POST /api/organizadores/login`
- Request body: `{ "mail": "string (formato email)", "password": "string" }`
- Response 200: body `{ "expiraEnUtc": "string (ISO 8601 UTC)" }` (SIN el JWT) + header
  `Set-Cookie: bingocart_auth=<jwt>; HttpOnly; Secure; SameSite=Strict; Path=/; Expires=<mismo
  expiraEnUtc>`.
- Response 400: validación de modelo (mail con formato inválido, campos vacíos) — manejo estándar
  ya existente vía `[ApiController]` (mismo comportamiento que `Registrar`).
- Response 401: credenciales inválidas O cuenta bloqueada (mismo body, mismo mensaje — ver Error
  handling) — `{ "error": "Credenciales inválidas." }` (formato ya definido por
  `ExceptionHandlingMiddleware` para las excepciones de dominio existentes). Sin `Set-Cookie`.
- Auth: ninguna para llamar al endpoint — es el que *otorga* autenticación. Requiere que el cliente
  llame con credenciales habilitadas (`withCredentials`/`credentials: 'include'`) para que el
  navegador acepte la cookie de la respuesta.
- CSRF: no se agrega token anti-CSRF en este ticket — `SameSite=Strict` ya evita que la cookie se
  envíe en requests cross-site (mitigación primaria), y el único endpoint protegido de este ticket
  (`GET /perfil`, Block 3) es de solo lectura sin efectos secundarios. Revisar cuando un ticket
  futuro agregue un endpoint protegido que modifique estado.

**Input validation**
- `Mail`: requerido, formato de email válido (`[EmailAddress]`), igual que en el registro.
- `Password`: requerido, no vacío. Sin validación de política de complejidad en el login (esa
  política ya se aplicó al crear la cuenta en FEAT-001a; el login solo verifica que coincida).

**Error handling**
- Mail no registrado → `CredencialesInvalidasException` (mismo mensaje que password incorrecta,
  AC-02).
- Password incorrecta → `CredencialesInvalidasException` (AC-02), y `SignInManager` incrementa
  internamente el contador de intentos fallidos de esa cuenta (comportamiento nativo de Identity,
  sin código adicional).
- Cuenta bloqueada (5° intento fallido ya alcanzado) → `CredencialesInvalidasException` con el
  MISMO mensaje que credenciales inválidas — decisión explícita para que un 401 nunca confirme por
  sí solo que la cuenta existe (un mensaje distinto para "bloqueada" filtraría la existencia de la
  cuenta a un atacante). Esta decisión queda sujeta a confirmación de `/daw-threat-modeling`.

**Required tests**
- [ ] `OrganizadorServiceTests` (unit, mock de `IIdentityGateway`): login con credenciales válidas
  devuelve `LoginOrganizadorResponse` con token no vacío — valida AC-01.
- [ ] `OrganizadorServiceTests`: login con `IIdentityGateway` devolviendo `CredencialesInvalidas`
  lanza `CredencialesInvalidasException` — valida AC-02.
- [ ] `OrganizadorServiceTests`: login con `IIdentityGateway` devolviendo `CuentaBloqueada` lanza
  la MISMA `CredencialesInvalidasException` con el mismo mensaje — valida AC-03 (parte del mensaje
  indistinguible).
- [ ] `OrganizadoresControllerTests` (integración, `WebApplicationFactory` + SQL Server real):
  registra un organizador de prueba, hace login con password correcta → 200, header `Set-Cookie`
  presente con `bingocart_auth=...; HttpOnly; Secure; SameSite=Strict`, body SIN campo `token`
  (solo `expiraEnUtc`) — verificación explícita de que el JWT nunca queda expuesto en el body,
  midiendo además con `Stopwatch` que la respuesta completa toma menos de 1 segundo — valida AC-01
  end-to-end y la estrategia de NFR-02.
- [ ] `OrganizadoresControllerTests`: login con password incorrecta → 401 con el mensaje genérico,
  sin header `Set-Cookie` — valida AC-02 end-to-end.
- [ ] `OrganizadoresControllerTests`: 5 intentos de login fallidos consecutivos contra la misma
  cuenta, seguidos de un 6° intento CON LA PASSWORD CORRECTA → 401 (cuenta bloqueada rechaza aunque
  la contraseña sea correcta) — valida AC-03 end-to-end, el escenario textual exacto del PRD.
  Limpieza: el organizador de prueba se borra en `DisposeAsync` (Regla #0).

**Completion criterion**
Los 6 tests listados pasan; un login exitoso real (contra el stack levantado) fija una cookie
`bingocart_auth` httpOnly con un JWT válido verificable con `JwtTokenService`, sin exponer el token
en el body de la respuesta; 5 fallos consecutivos bloquean la cuenta por 5 minutos incluso con la
password correcta en el 6° intento.

## Block 3 — Endpoint protegido de verificación (`GET /api/organizadores/perfil`)

**Files**
- `backend/BingoCart.Api/Controllers/OrganizadoresController.cs` (modified) — agrega `[Authorize]
  [HttpGet("perfil")] public ActionResult<PerfilOrganizadorResponse> Perfil()`, que lee el claim
  `Email` del `ClaimsPrincipal` autenticado (`User.FindFirstValue(ClaimTypes.Email)`) y lo devuelve.
  **Excepción explícita y documentada al patrón de capas** (comentario en el código remitiendo a
  este punto del spec): no llama a ningún servicio de `IOrganizadorService` — el claim ya viene
  verificado por el middleware de `AddJwtBearer` (Block 1), así que no hay ninguna consulta ni regla
  de negocio que ejecutar; delegar a Application agregaría una capa sin propósito para un endpoint
  cuyo único fin es la verificación de infraestructura descrita en el Summary.
- `backend/BingoCart.Api/Contracts/PerfilOrganizadorResponse.cs` (new) — `record
  PerfilOrganizadorResponse(string Mail);`. Vive en `Api/Contracts/` (carpeta nueva, primera de su
  tipo en el proyecto) y NO en `Application/Organizadores/Dtos/` — corrección tras la revisión de
  `daw-arch-auditor` en PLAN: ningún servicio de Application construye ni toca este DTO, así que
  ubicarlo en Application habría sido engañoso (sugiere un flujo por esa capa que no existe).

**Logic**
Endpoint mínimo cuyo único propósito es hacer verificable, con una request HTTP real, que el
middleware de autenticación JWT (Block 1) rechaza tokens inválidos/expirados y acepta los válidos —
decisión tomada explícitamente con el usuario en PLAN (ver Summary).

**API contract**
- Method + path: `GET /api/organizadores/perfil`
- Request: sin body; el JWT viaja en la cookie `bingocart_auth` (enviada automáticamente por el
  navegador si la request usa `withCredentials`/`credentials: 'include'` — Block 1 la extrae de
  `Request.Cookies` vía `JwtBearerEvents.OnMessageReceived`).
- Response 200: `{ "mail": "string" }`
- Response 401: cookie ausente, token inválido o expirado (manejado automáticamente por el
  middleware de `AddJwtBearer` de Block 1, sin código adicional en el controller).
- Auth: JWT Bearer vía cookie requerido (`[Authorize]`).

**Input validation**
N/A — no recibe body ni parámetros, solo el header de autorización estándar.

**Error handling**
Delegado 100% al pipeline de `AddJwtBearer`/`[Authorize]` de ASP.NET Core (configurado en Block 1)
— ningún manejo de errores adicional en este bloque.

**Required tests**
- [ ] `OrganizadoresControllerTests`: request a `/perfil` sin cookie `bingocart_auth` → 401.
- [ ] `OrganizadoresControllerTests`: login real contra el mismo `HttpClient` (con
  `HttpClientHandler.UseCookies = true`, o capturando y reenviando el `Set-Cookie` manualmente,
  simulando el comportamiento real del navegador), seguido de un request a `/perfil` con la cookie
  ya presente → 200 con el mail correcto — valida que el pipeline de autenticación vía cookie
  funciona end-to-end (soporte de AC-01/FR-02).
- [ ] `OrganizadoresControllerTests`: request a `/perfil` con el header `Cookie: bingocart_auth=
  <token>` seteado manualmente, usando un token generado por un `JwtTokenService` construido con el
  `TestTimeProvider` de Block 1 (reloj ya 61 minutos en el pasado) → 401 — valida AC-04 exactamente
  como lo describe el PRD, sin esperar 60 minutos reales en el test.

**Completion criterion**
Los 3 tests pasan; un token expirado (simulado sin esperar el plazo real) es rechazado por una
request HTTP real contra un endpoint protegido, cerrando la brecha de verificabilidad de AC-04
identificada en PLAN.

## Block 4 — Frontend: formulario de login

**Files**
- `frontend/src/app/features/auth/services/auth.service.ts` (new) — `AuthService`
  (`providedIn: 'root'`, mismo patrón que `OrganizadorService`), método `login(mail, password):
  Observable<LoginResponse>` que llama a `POST /api/organizadores/login` con
  `{ withCredentials: true }` (imprescindible para que el navegador acepte y reenvíe la cookie
  httpOnly `bingocart_auth` — decisión de `/daw-threat-modeling` en PLAN). **No persiste ningún
  token** — la cookie httpOnly no es legible ni escribible desde JavaScript por diseño, así que no
  hay nada que guardar en `localStorage`. El `expiraEnUtc` recibido en el body se guarda en un
  `BehaviorSubject` en memoria (para que la UI sepa si "hay sesión" durante la vida de la pestaña),
  sin persistencia entre recargas de página — limitación aceptada explícitamente: no hay
  interceptor ni guard todavía (fuera de alcance de este ticket, señalado como gap conocido por el
  impact scan; queda para un ticket posterior que consuma la sesión en llamadas protegidas, y que
  probablemente necesite un endpoint tipo `/perfil` —ya construido en Block 3— para reconstruir el
  estado de sesión al recargar la página). **Por qué un `AuthService`
  separado y no un método más en `OrganizadorService`** (aclarado tras la revisión de
  `daw-arch-auditor` en PLAN): aunque el recurso HTTP es el mismo controller, autenticación es un
  concern transversal (va a crecer con interceptor, guard y potencialmente refresh en tickets
  futuros) mientras que `OrganizadorService` es específicamente el service de gestión de la cuenta
  del organizador — la separación anticipa esa divergencia en vez de forzar responsabilidades no
  relacionadas en un mismo service.
- `frontend/src/app/features/auth/models/login-request.model.ts` (new) — interfaz `LoginRequest {
  mail: string; password: string; }`, `readonly` en sus propiedades.
- `frontend/src/app/features/auth/models/login-response.model.ts` (new) — interfaz `LoginResponse {
  expiraEnUtc: string; }`, `readonly` — sin campo `token` (nunca llega al frontend en el body, ver
  Block 2).
- `frontend/src/app/features/auth/components/login-organizador/login-organizador.component.ts`
  (new) — formulario reactivo (`ReactiveFormsModule`, mismo patrón que
  `registro-organizador.component.ts`), campos mail/password, manejo de error por campo, y un
  mensaje de error general (401) mostrado sin distinguir causa (coherente con Block 2).
- `frontend/src/app/features/auth/components/login-organizador/login-organizador.component.html`
  (new)
- `frontend/src/app/features/auth/components/login-organizador/login-organizador.component.scss`
  (new)
- `frontend/src/app/features/auth/auth-routing.module.ts` (modified) — agrega la ruta `login`
  apuntando al nuevo componente, hermana de la ruta `registro` existente.
- `frontend/src/app/features/auth/auth.module.ts` (modified) — declara el nuevo componente.

**Logic**
El componente delega 100% la llamada HTTP a `AuthService` (nunca `HttpClient` directo en el
componente, coherente con AGENTS.md — "el FrontEnd nunca llama directamente a la API sin pasar por
un service Angular dedicado"). Tras un login exitoso, redirige a una ruta placeholder (a definir:
por ahora, la home `/`, ya que no existe todavía ningún dashboard de organizador que sea el destino
natural — RF-02 en adelante).

**Input validation**
- `mail`: `Validators.required`, `Validators.email`.
- `password`: `Validators.required`.

**Error handling**
- 401 del backend → mensaje genérico en el formulario ("Credenciales inválidas."), sin distinguir
  causa, coherente con el backend (Block 2).
- Error de red/5xx → manejado por el interceptor HTTP global ya existente
  (`http-error.interceptor.ts`, de FEAT-001a), sin lógica nueva en este bloque — ya cubierto por
  los tests existentes de ese interceptor, no se agrega un test nuevo acá.

**Required tests**
- [ ] `AuthService` (unit, `HttpClientTestingModule`): `login()` hace `POST` a la URL correcta con
  el body esperado y `withCredentials: true`, y actualiza el `BehaviorSubject` en memoria con el
  `expiraEnUtc` recibido tras una respuesta 200.
- [ ] `LoginOrganizadorComponent` (unit): envío del formulario con datos válidos invoca
  `AuthService.login()`.
- [ ] `LoginOrganizadorComponent` (unit): un 401 simulado muestra el mensaje de error genérico sin
  redirigir.
- [ ] E2E (Playwright, mismo patrón que `RegistroOrganizadorE2ETests.cs`): registra un organizador
  de prueba (reutilizando el flujo de FEAT-001a), hace login con las credenciales correctas contra
  el stack contenedorizado real, y verifica — inspeccionando las cookies del contexto del
  navegador vía la API de Playwright — que `bingocart_auth` quedó seteada con `httpOnly: true` —
  cierra el loop completo (AC-01 end-to-end, frontend incluido). Limpieza del organizador de
  prueba en `DisposeAsync`.

**Completion criterion**
Los 4 tests pasan; un login real contra el stack contenedorizado, desde el formulario, deja una
cookie httpOnly válida (verificable vía Playwright), sin que el token quede accesible desde
JavaScript ni persistido en `localStorage`.

## Final verification

`dotnet build`/`dotnet test` sobre `backend/BingoCart.sln` en verde, incluyendo los 16 tests nuevos
de los Blocks 1-3 (5+6+5). Frontend: tests unitarios de Angular en verde. Suite E2E completa
(`RegistroOrganizadorE2ETests` + el nuevo test de login) en verde contra el stack contenedorizado,
confirmando con la API de cookies de Playwright que `bingocart_auth` es `httpOnly`. Un login real
con 5 intentos fallidos previos rechaza el 6° intento aunque la password sea correcta, y un token
con expiración pasada es rechazado por `/perfil` — ambos verificados con requests HTTP reales, no
solo a nivel de unidad. El JWT nunca aparece en ningún body de respuesta HTTP ni en `localStorage`
— solo en la cookie `httpOnly`.
