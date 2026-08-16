# Spec FEAT-001a: Registro de organizador

| Field | Value |
|-------|-------|
| Ticket | FEAT-001a |
| PRD | docs/daw/prd/prd-FEAT-001a.md |
| Tier | FEATURE |
| Date | 2026-08-15 |
| Spec loops | 2 |

## Summary

Se levanta desde cero la solución (backend .NET 8 por capas + frontend Angular 18 + SQL Server
dockerizado) y se implementa el registro de organizador end-to-end: un `Organizador` es una entidad
de Domain con sus propias invariantes (CUIT, teléfono), la persistencia de credenciales se delega a
ASP.NET Core Identity detrás de un puerto (`IIdentityGateway`) para no acoplar la capa de Aplicación
al tipo concreto de Infrastructure, y el frontend expone un formulario reactivo que espeja las
validaciones del backend. Cierra con la containerización completa del stack para pruebas manuales.

## Coverage: PRD → blocks

| Requirement | Covered by |
|---|---|
| FR-01 | Block 2, Block 3, Block 4, Block 6 |
| FR-02 | Block 2 |
| FR-03 | Block 3, Block 4 |
| FR-04 | Block 3 |
| FR-05 | Block 2 |
| FR-06 | Block 3 |
| FR-07 | Block 3 |
| NFR-01 | Strategy: hashing delegado íntegramente a `UserManager<ApplicationUser>.CreateAsync` (ASP.NET Core Identity, PBKDF2+salt por defecto) — nunca se maneja el password como texto plano fuera de ese límite (Block 3) |
| NFR-02 | Strategy: `ExceptionHandlingMiddleware` (Block 4) nunca loguea CUIT/mail/teléfono; acceso a datos restringido por rol se hereda de que el endpoint de registro es el único punto de escritura y no expone lectura de otros organizadores (fuera de alcance de este ticket) |
| NFR-03 | Strategy: endpoint de registro sin llamadas externas ni operaciones costosas — una escritura a SQL Server vía Identity; medido en Block 4 |

## Dependencies between blocks

- Block 2 depende de Block 1 (necesita el proyecto `BingoCart.Domain` creado).
- Block 3 depende de Block 1 y Block 2 (necesita `Organizador` y el esqueleto de `Program.cs`).
- Block 4 depende de Block 1 y Block 3.
- Block 5 no tiene dependencia técnica real con el backend — puede correr en paralelo a Block 2-4.
- Block 6 depende de Block 4 (necesita el endpoint) y Block 5 (necesita el workspace Angular).
- Block 7 depende de Block 4 y Block 6 (necesita backend y frontend completos para dockerizarlos).

Orden sugerido: 1 → 2 → 3 → 4 → (5 en paralelo desde el inicio) → 6 → 7.

**Decisiones de arquitectura cerradas con el usuario (no reabrir en CODE):**
(a) `Organizador` es una entidad de Domain propia con factory de creación (`Organizador.Crear`), no
solo columnas de `ApplicationUser`.
(b) `OrganizadorService` depende de la interfaz `IIdentityGateway` (puerto en Application), nunca de
`UserManager` directamente — `IdentityGateway` (Infrastructure) es la única clase que conoce
`UserManager<ApplicationUser>`.

## Block 1 — Infraestructura base backend

**Files**
- `BingoCart.sln` (new)
- `docker-compose.yml` (new) — solo servicio `db` (SQL Server 2022, puerto 14330)
- `src/BingoCart.Domain/BingoCart.Domain.csproj` (new) — `<Nullable>enable</Nullable>`
- `src/BingoCart.Application/BingoCart.Application.csproj` (new) — `<Nullable>enable</Nullable>`
- `src/BingoCart.Infrastructure/BingoCart.Infrastructure.csproj` (new) — `<Nullable>enable</Nullable>`
- `src/BingoCart.Api/BingoCart.Api.csproj` (new) — `<Nullable>enable</Nullable>`
- `src/BingoCart.Api/Program.cs` (new) — registra `AppDbContext` (SQL Server), `AddIdentity<ApplicationUser, IdentityRole<Guid>>().AddEntityFrameworkStores<AppDbContext>()`, Swagger, `AddControllers()`/`MapControllers()`. Sin JWT (queda para FEAT-001b).
- `src/BingoCart.Api/appsettings.json` (new) — sin secretos; la connection string real se inyecta
  por variable de entorno (`ConnectionStrings__Default`), nunca hardcodeada
- `src/BingoCart.Api/appsettings.Development.json` (new) — connection string local a `db` (Docker),
  con `Encrypt=True` (cifrado en tránsito hacia SQL Server, F-TM-07)
- `src/BingoCart.Infrastructure/Identity/ApplicationUser.cs` (new) — `IdentityUser<Guid>` + `NombreOrganizacion`, `Cuit`, `Telefono`
- `src/BingoCart.Infrastructure/Data/AppDbContext.cs` (new) — `IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`
- `src/BingoCart.Infrastructure/Data/Migrations/` (new) — migración `InitialCreate`

**Logic**
Scaffolding puro: solución de 4 proyectos por capa, contenedor de SQL Server, `DbContext` de
Identity extendido con los campos de negocio del organizador, y el composition root (`Program.cs`)
con lo mínimo para que la app arranque (DB + Identity + Swagger + ruteo de controllers). Ningún
requerimiento funcional se implementa todavía en este bloque.

**Data model**
- Tabla `AspNetUsers` (generada por Identity, extendida): además de las columnas propias de
  `IdentityUser<Guid>` (`Id` PK, `Email`, `NormalizedEmail` único, `PasswordHash`, `PhoneNumber`,
  `LockoutEnd`, `AccessFailedCount`, etc.), agrega:
  - `NombreOrganizacion` (`nvarchar(200)`, not null)
  - `Cuit` (`nvarchar(11)`, not null, índice único)
  - `Telefono` (`nvarchar(20)`, not null)

**Input validation**
N/A — bloque de infraestructura, sin input de usuario.

**Error handling**
N/A — bloque de infraestructura, sin lógica de negocio.

**Required tests**
- [ ] `AppDbContextTests.PuedeConstruirseYAplicarMigracion` — construye `AppDbContext` contra el SQL
  Server dockerizado y aplica `InitialCreate` sin error.

**Completion criterion**
`dotnet build` sin errores en la solución completa. `dotnet ef database update` aplica limpio contra
el contenedor `db`. `GET /swagger` responde 200.

**Seguridad (threat model, ver `docs/daw/security/threat-FEAT-001a.md`)**
- Cifrado en tránsito hacia SQL Server: `Encrypt=True` en la connection string (arriba).
- Cifrado at-rest de datos personales (CUIT, mail, teléfono, hash de password): habilitar
  Transparent Data Encryption (TDE) en la instancia de SQL Server del `docker-compose.yml`.
- Ninguna credencial real en `appsettings.json` versionado — solo por variable de entorno.

**Rollback**
`InitialCreate` es la primera migración sobre una base de datos vacía (sin datos de producción
todavía): revertirla es `dotnet ef database update 0` seguido de `dotnet ef migrations remove`, o
directamente recrear el contenedor `db` (`docker-compose down -v && docker-compose up db`).

## Block 2 — Dominio: entidad Organizador y validadores puros

**Files**
- `src/BingoCart.Domain/Organizadores/Organizador.cs` (new) — entidad con constructor privado +
  factory estático `Organizador.Crear(nombreOrganizacion, cuit, mail, telefono)`
- `src/BingoCart.Domain/Organizadores/CuitValidator.cs` (new) — clase estática pura
- `src/BingoCart.Domain/Organizadores/TelefonoValidator.cs` (new) — clase estática pura
- `src/BingoCart.Domain/Organizadores/Exceptions/CuitInvalidoException.cs` (new)
- `src/BingoCart.Domain/Organizadores/Exceptions/TelefonoInvalidoException.cs` (new)
- `src/BingoCart.Domain/Organizadores/Exceptions/MailYaRegistradoException.cs` (new)
- `src/BingoCart.Domain/Common/DomainException.cs` (new) — clase base abstracta

**Logic**
`Organizador.Crear(...)` invoca `CuitValidator` y `TelefonoValidator` (ambos funciones puras, sin
I/O) y lanza la excepción tipada correspondiente si algo es inválido; si todo es válido, devuelve
una instancia inmutable (propiedades `init`-only: `Id` (`Guid` nuevo), `NombreOrganizacion`, `Cuit`,
`Mail`, `Telefono`). `MailYaRegistradoException` se define aquí (es del dominio) pero se lanza desde
Application (Block 3), tras consultar `IIdentityGateway`.

**Input validation**
- CUIT: exactamente 11 dígitos numéricos + dígito verificador válido según el algoritmo estándar
  CUIT/CUIL argentino.
- Teléfono: numérico (dígitos, `+`, espacios y guiones permitidos), longitud entre 8 y 20
  caracteres.

**Error handling**
- CUIT con formato o dígito verificador inválido → `CuitInvalidoException`, mensaje describe qué
  regla se violó (longitud incorrecta / dígito verificador inválido), sin exponer el CUIT completo
  en el mensaje.
- Teléfono con formato o longitud inválida → `TelefonoInvalidoException`, mismo criterio de mensaje.

**Required tests**
- [ ] `OrganizadorTests.Crear_ConDatosValidos_CreaLaEntidad` — valida AC-01
- [ ] `OrganizadorTests.Crear_ConCuitDeLongitudIncorrecta_LanzaCuitInvalidoException` — valida AC-02
- [ ] `OrganizadorTests.Crear_ConDigitoVerificadorInvalido_LanzaCuitInvalidoException` — valida AC-02
- [ ] `OrganizadorTests.Crear_ConTelefonoNoNumerico_LanzaTelefonoInvalidoException` — valida AC-05
- [ ] `OrganizadorTests.Crear_ConTelefonoFueraDeRango_LanzaTelefonoInvalidoException` — valida AC-05
  (< 8 y > 20 caracteres)
- [ ] `CuitValidatorTests` — casos límite: CUIT válido conocido, longitud 10/12, dígito verificador
  alterado en 1 posición
- [ ] `TelefonoValidatorTests` — casos límite: longitud exacta 8 y 20 (válidos), 7 y 21 (inválidos),
  formatos con `+`/espacios/guiones válidos e inválidos

**Completion criterion**
Todos los tests unitarios en verde. 100% branch coverage en `Organizador.Crear`, `CuitValidator` y
`TelefonoValidator`.

## Block 3 — Aplicación: puerto de Identity y servicio de registro

**Files**
- `src/BingoCart.Application/Organizadores/IIdentityGateway.cs` (new) — puerto
- `src/BingoCart.Application/Organizadores/Dtos/RegistrarOrganizadorRequest.cs` (new) — `record`
  con DataAnnotations: `NombreOrganizacion` `[Required, StringLength(200, MinimumLength = 1)]`,
  `Mail` `[Required, EmailAddress]`
- `src/BingoCart.Application/Organizadores/Dtos/RegistrarOrganizadorResponse.cs` (new) — `record`
- `src/BingoCart.Application/Organizadores/IOrganizadorService.cs` (new)
- `src/BingoCart.Application/Organizadores/OrganizadorService.cs` (new)
- `src/BingoCart.Domain/Organizadores/Exceptions/PasswordInvalidaException.cs` (new)
- `src/BingoCart.Infrastructure/Identity/IdentityGateway.cs` (new) — implementa `IIdentityGateway`
- `src/BingoCart.Infrastructure/Identity/IdentityConfiguration.cs` (new) — política de password
- `src/BingoCart.Api/Program.cs` (modified) — registra `IOrganizadorService`→`OrganizadorService` y
  `IIdentityGateway`→`IdentityGateway` en DI (scoped); aplica `IdentityConfiguration` sobre el
  builder de Identity ya registrado en Block 1

**Logic**
`IIdentityGateway` expone `Task<bool> ExisteMailAsync(string mail)` y
`Task<IdentityGatewayResult> CrearUsuarioAsync(Organizador organizador, string password)`
(`IdentityGatewayResult` es un `record` con `Exitoso: bool` y `Errores: IReadOnlyList<string>`).
`OrganizadorService.RegistrarAsync`: (1) `Organizador.Crear(...)` — valida CUIT/teléfono, propaga la
excepción de Domain si es inválido; (2) `gateway.ExisteMailAsync(mail)` — si `true`, lanza
`MailYaRegistradoException`; (3) `gateway.CrearUsuarioAsync(organizador, password)` — si falla por
política de password, lanza `PasswordInvalidaException` con el detalle de
`IdentityGatewayResult.Errores`; si tiene éxito, devuelve `RegistrarOrganizadorResponse`.
`IdentityGateway` (Infrastructure) es la única clase que conoce `UserManager<ApplicationUser>`:
`ExisteMailAsync` usa `FindByEmailAsync`; `CrearUsuarioAsync` mapea `Organizador`→`ApplicationUser`
(copia `NombreOrganizacion`/`Cuit`/`Mail`/`Telefono`, `UserName = Mail`, `EmailConfirmed = true` —
activación inmediata sin verificación de mail, FR-06) y llama `UserManager.CreateAsync(user,
password)`, traduciendo `IdentityResult.Errors` a `IdentityGatewayResult`.

**Input validation**
- Password: política por defecto de ASP.NET Core Identity — mínimo 8 caracteres, 1 mayúscula, 1
  minúscula, 1 dígito, 1 carácter no alfanumérico (`IdentityConfiguration.cs`). Delegada
  íntegramente a Identity vía el gateway, no reimplementada a mano.
- `NombreOrganizacion`: requerido, no vacío, máximo 200 caracteres — `[Required]`,
  `[StringLength(200, MinimumLength = 1)]` en `RegistrarOrganizadorRequest` (DataAnnotations).
- `Mail`: requerido, formato de mail válido — `[Required]`, `[EmailAddress]` en
  `RegistrarOrganizadorRequest`. La unicidad se valida en Block 3/4 (`MailYaRegistradoException`),
  el formato se valida acá.

**Error handling**
- CUIT inválido → `CuitInvalidoException` (Block 2), propagada sin llamar al gateway.
- Teléfono inválido → `TelefonoInvalidoException` (Block 2), propagada sin llamar al gateway.
- Mail ya registrado → `MailYaRegistradoException`, no se llama a `CrearUsuarioAsync`.
- Password inválida → `PasswordInvalidaException` con el detalle de reglas incumplidas.

**Required tests**
- [ ] `OrganizadorServiceTests.RegistrarAsync_ConDatosValidos_DevuelveResponseCorrecto` — valida
  AC-01 (con `IIdentityGateway` mockeado)
- [ ] `OrganizadorServiceTests.RegistrarAsync_ConCuitInvalido_LanzaExcepcionYNoLlamaAlGateway` —
  valida AC-02
- [ ] `OrganizadorServiceTests.RegistrarAsync_ConMailDuplicado_LanzaMailYaRegistradoException` —
  valida AC-03
- [ ] `OrganizadorServiceTests.RegistrarAsync_ConPasswordInvalida_LanzaPasswordInvalidaException` —
  valida AC-04
- [ ] `OrganizadorServiceTests.RegistrarAsync_ConTelefonoInvalido_LanzaExcepcionYNoLlamaAlGateway`
  — valida AC-05

**Completion criterion**
Todos los tests unitarios en verde. ≥80% line/branch coverage en `OrganizadorService`.

## Block 4 — API: endpoint de registro

**Files**
- `src/BingoCart.Api/Controllers/OrganizadoresController.cs` (new)
- `src/BingoCart.Api/Middleware/ExceptionHandlingMiddleware.cs` (new)
- `src/BingoCart.Api/Program.cs` (modified) — engancha `app.UseMiddleware<ExceptionHandlingMiddleware>()` antes de `UseAuthorization`/`MapControllers`; configura `InvalidModelStateResponseFactory` para que un `ModelState` inválido (DataAnnotations de `NombreOrganizacion`/`Mail`, Block 3) devuelva el mismo contrato `{ "error": "DatosInvalidos", "message" }` en vez del `ValidationProblemDetails` por defecto de ASP.NET; registra `AddRateLimiter()` con la política fija `"registro"` (5 req/min por IP) y `app.UseRateLimiter()` en el pipeline

**Logic**
`OrganizadoresController` (`[ApiController]`, `[Route("api/organizadores")]`) expone
`RegistrarAsync` (`[HttpPost("registro")]`, `[AllowAnonymous]`, `[EnableRateLimiting("registro")]`):
recibe `RegistrarOrganizadorRequest`, llama a `IOrganizadorService.RegistrarAsync`, devuelve 201 con
`RegistrarOrganizadorResponse`. Nunca serializa `Organizador` ni `ApplicationUser` directamente. Tras
un registro exitoso, loguea un evento INFO de auditoría (`"Organizador registrado: {Id}"`, solo el
`Guid` generado — nunca CUIT/mail/teléfono) para trazabilidad (mitiga el hueco de repudio detectado
en el threat model). `ExceptionHandlingMiddleware` captura `DomainException` y subtipos, mapea a
HTTP; cualquier excepción no controlada → 500 genérico sin stack trace. El log interno (`ILogger`)
de cada excepción **nunca incluye CUIT, mail ni teléfono** — solo tipo de excepción, timestamp y un
correlation id (NFR-02).

**Seguridad (threat model)**
- Rate limiting: política fija `"registro"` (`Microsoft.AspNetCore.RateLimiting`, ya incluido en
  .NET 8 — sin dependencia nueva), 5 solicitudes/minuto por IP sobre este endpoint. Mitiga spam/DoS
  sobre un endpoint público sin autenticación. Configurada en `Program.cs` (este bloque).

**API contract**
- Method + path: `POST /api/organizadores/registro`
- Auth: ninguna (`[AllowAnonymous]`, endpoint público)
- Request: `{ "nombreOrganizacion": string, "cuit": string, "mail": string, "telefono": string, "password": string }`
- Response 201: `{ "id": "guid", "nombreOrganizacion": string, "mail": string }`
- Response 400: `{ "error": "CuitInvalido" | "TelefonoInvalido" | "PasswordInvalida" | "DatosInvalidos", "message": string }`
- Response 409: `{ "error": "MailYaRegistrado", "message": string }`
- Response 500: `{ "error": "ErrorInterno", "message": string genérico }`
- Error codes: 400, 409, 500 (ver arriba)

**Input validation**
Delegada a Domain (Block 2) y Application (Block 3); el controller no revalida nada, solo orquesta.

**Error handling**
- `CuitInvalidoException` → 400 `CuitInvalido`
- `TelefonoInvalidoException` → 400 `TelefonoInvalido`
- `PasswordInvalidaException` → 400 `PasswordInvalida`
- `MailYaRegistradoException` → 409 `MailYaRegistrado`
- `ModelState` inválido (`NombreOrganizacion`/`Mail` con DataAnnotations violadas) → 400
  `DatosInvalidos`
- No controlada → 500 `ErrorInterno` (mensaje genérico, sin stack trace ni PII)

**Required tests**
- [ ] `OrganizadoresControllerTests.Registro_ConDatosValidos_Devuelve201` — valida AC-01
- [ ] `OrganizadoresControllerTests.Registro_ConCuitInvalido_Devuelve400CuitInvalido` — valida AC-02
- [ ] `OrganizadoresControllerTests.Registro_ConMailDuplicado_Devuelve409MailYaRegistrado` — valida
  AC-03
- [ ] `OrganizadoresControllerTests.Registro_ConPasswordInvalida_Devuelve400PasswordInvalida` —
  valida AC-04
- [ ] `OrganizadoresControllerTests.Registro_ConTelefonoInvalido_Devuelve400TelefonoInvalido` —
  valida AC-05
- [ ] `OrganizadoresControllerTests.Registro_ConDatosValidos_PasswordSeAlmacenaComoHash` — valida
  AC-06 (consulta `AppDbContext` directamente tras el registro)
- [ ] `OrganizadoresControllerTests.Registro_ConNombreOrganizacionVacioOMailMalformado_Devuelve400DatosInvalidos`
  — cubre el `ModelState` inválido (F-SPEC-09)
- [ ] `ExceptionHandlingMiddlewareTests.Invoke_AnteExcepcionNoControlada_Devuelve500ErrorInternoSinDetalles`
  — test de middleware aislado (`next()` que lanza una `Exception` genérica), verifica 500 con el
  body genérico y sin stack trace ni datos personales

**Completion criterion**
Los 6 tests de integración (`WebApplicationFactory<Program>` contra SQL Server dockerizado) pasan en
verde. Swagger documenta el contrato completo con los 4 códigos de respuesta.

## Block 5 — Infraestructura base frontend

**Files**
- `angular.json`, `package.json`, `tsconfig.json`, `tsconfig.app.json` (new)
- `tailwind.config.js`, `postcss.config.js` (new)
- `src/main.ts`, `src/index.html`, `src/styles.scss` (new)
- `src/app/app.module.ts`, `src/app/app-routing.module.ts` (new) — esqueleto, solo ruta raíz
- `src/app/app.component.ts`/`.html`/`.scss` (new)
- `src/app/core/core.module.ts` (new)
- `src/app/core/interceptors/http-error.interceptor.ts` (new)
- `src/environments/environment.ts`, `environment.prod.ts` (new) — `apiUrl: http://localhost:8080`

**Logic**
Workspace Angular 18 con Tailwind + Angular Material. `http-error.interceptor.ts` normaliza errores
HTTP no manejados explícitamente por un componente (5xx, errores de red); NO reemplaza el manejo de
error por campo que cada componente debe hacer (ver Block 6).

**Input validation**
N/A.

**Error handling**
El interceptor cubre errores HTTP genéricos; el manejo de errores de negocio por campo es
responsabilidad de cada componente.

**Required tests**
- [ ] `AppComponent` smoke test (Jasmine/Karma, default de Angular CLI) — se crea correctamente

**Completion criterion**
`ng build` sin errores. `ng serve` responde en `http://localhost:8000`. Tailwind y Angular Material
aplican estilos a una página placeholder.

## Block 6 — Frontend: formulario de registro de organizador

**Files**
- `src/app/features/auth/auth.module.ts` (new)
- `src/app/features/auth/auth-routing.module.ts` (new) — ruta `registro`
- `src/app/features/auth/components/registro-organizador/registro-organizador.component.ts` (new)
- `src/app/features/auth/components/registro-organizador/registro-organizador.component.html` (new)
- `src/app/features/auth/components/registro-organizador/registro-organizador.component.scss` (new)
- `src/app/features/auth/services/organizador.service.ts` (new)
- `src/app/features/auth/models/registrar-organizador-request.model.ts` (new) — interface, sin `any`
- `src/app/features/auth/models/registrar-organizador-response.model.ts` (new) — interface, sin `any`
- `src/app/app-routing.module.ts` (modified) — agrega ruta lazy-loaded hacia `AuthModule`

**Logic**
Formulario reactivo (`FormBuilder`): `nombreOrganizacion`, `cuit`, `mail`, `telefono`, `password` —
validadores síncronos de Angular espejando las reglas del backend (`cuit` con `Validators.pattern`
de 11 dígitos, `telefono` con patrón numérico 8-20 chars, `password` con `Validators.minLength(8)` +
patrones de complejidad, `mail` con `Validators.email`). Al enviar, llama a
`OrganizadorService.registrar(request)` — único punto de acceso a la API para este feature, el
componente nunca usa `HttpClient` directamente. En éxito (201) muestra mensaje de éxito. En error,
mapea el código del body (`CuitInvalido`/`TelefonoInvalido`/`PasswordInvalida`/`MailYaRegistrado`) a
un mensaje inline en el campo correspondiente del formulario — no delega esto solo al interceptor
global (Block 5).

**Input validation**
Espejo cliente de las validaciones de backend (CUIT, teléfono, password, mail) — UX únicamente, la
validación autoritativa sigue siendo el backend (Block 2/3).

**Error handling**
Mapeo de código de error de negocio → mensaje de campo específico (los 4 códigos del contrato de
Block 4), más el interceptor genérico de Block 5 para errores no esperados (5xx, red).

**Required tests**
- [ ] Formulario inválido si falta cualquier campo requerido
- [ ] Validador de CUIT rechaza formato inválido en el cliente
- [ ] `OrganizadorService.registrar` llama al endpoint correcto con el payload correcto
  (`HttpClientTestingModule`)
- [ ] Componente muestra el mensaje de error correcto en el campo correcto ante cada código de
  error del backend (mockeando la respuesta HTTP)
- [ ] Playwright E2E — flujo feliz completo (AC-01)
- [ ] Playwright E2E — flujo de mail duplicado (AC-03)

**Completion criterion**
Tests unitarios en verde. Los 2 tests E2E de Playwright pasan contra el stack completo levantado.

## Block 7 — Containerización completa

**Files**
- `src/BingoCart.Api/Dockerfile` (new) — multi-stage: `sdk:8.0` build → `aspnet:8.0` runtime
- Dockerfile del frontend, raíz del proyecto Angular (new) — multi-stage: `node:20` build →
  `nginx:alpine` serve, puerto 8000
- `.dockerignore` (new, raíz y por proyecto si aplica)
- `docker-compose.yml` (modified) — agrega servicio `api` (build desde
  `src/BingoCart.Api/Dockerfile`, puerto 8080, `depends_on: db`, connection string por variable de
  entorno) y servicio `web` (build desde el Dockerfile del frontend, puerto 8000, `depends_on: api`,
  `apiUrl` por variable de entorno)

**Logic**
Containeriza los servicios ya completos (backend Block 1-4, frontend Block 5-6) para que
`docker-compose up --build` deje todo el stack listo para pruebas manuales.

**Seguridad (threat model)**
El puerto de `db` (14330) queda expuesto al host vía `docker-compose.yml` (Block 1) — aceptable
para desarrollo local, donde este ticket se ejecuta. Nota operativa para cualquier entorno
compartido o productivo futuro (fuera de alcance de este ticket): el servicio `db` no debe exponer
su puerto fuera de la red interna de `docker-compose`, solo `api` y `web` deben ser alcanzables
externamente.

**Input validation**
N/A.

**Error handling**
N/A — configuración de despliegue, no código de aplicación.

**Required tests**
- [ ] `docker-compose up --build` termina con los 3 servicios (`db`, `api`, `web`) en estado
  `running`/healthy
- [ ] `curl http://localhost:8080/swagger` responde 200
- [ ] `curl http://localhost:8000` responde 200
- [ ] Verificación manual: completar el formulario de registro contra los contenedores y confirmar
  que el organizador queda persistido (AC-01 end-to-end contenedorizado)

**Completion criterion**
`docker-compose up --build` levanta `db`+`api`+`web` sin errores. `http://localhost:8080/swagger`
responde 200. `http://localhost:8000` responde 200. El flujo completo de registro (AC-01) funciona
end-to-end contra los contenedores, verificado manualmente.

## Final verification

Los 6 AC del PRD (`docs/daw/prd/prd-FEAT-001a.md`) tienen al menos un test automatizado en verde
(Block 2, 3, 4 y 6). `docker-compose up --build` deja el stack completo operable para prueba manual
del flujo de registro. Ningún dato personal (CUIT, mail, teléfono) aparece en logs de aplicación
(NFR-02, verificado en Block 4). Cobertura ≥80% en `OrganizadorService` (NFR global de
`.daw/rules/testing.instructions.md`).
