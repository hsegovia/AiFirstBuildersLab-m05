# Spec FEAT-010a: Infraestructura transversal y resolución de rol (whoami)

| Field | Value |
|-------|-------|
| Ticket | FEAT-010a |
| PRD | docs/daw/prd/prd-FEAT-010a.md |
| Tier | FEATURE |
| Date | 2026-08-25 |
| Spec loops | 0 |

## Summary

Un endpoint de backend nuevo y mínimo (`GET /api/auth/whoami`) resuelve el rol de la sesión activa
leyendo los claims del JWT ya emitido, sin exponerlo al cliente. Encima de él, el frontend construye
un `SessionService` (única fuente de verdad de sesión, reemplaza el `BehaviorSubject` sin
consumidores de `AuthService`), un interceptor HTTP extendido que maneja 401/403/429 de forma
centralizada, dos guards de rol, y el layout base (`AppComponent` como shell puro + `HomeComponent` +
un componente de paginación compartido que FEAT-010b/c/e van a reutilizar). Cierra con tests E2E
(Playwright .NET) para los comportamientos que solo se pueden verificar contra un navegador real
(refresh de página, redirección de guard, cookie `httpOnly`).

## Coverage: PRD → blocks

| Requirement | Covered by |
|---|---|
| FR-01 (whoami) | Block 1, Block 6 (E2E) |
| FR-02 (layout por rol) | Block 2, Block 5 |
| FR-03 (interceptor) | Block 3 |
| FR-04 (guards) | Block 4, Block 6 (E2E) |
| NFR-01 (sin localStorage) | Strategy: `SessionService` guarda el rol/mail solo en un `BehaviorSubject` en memoria (Angular), nunca en `localStorage`/`sessionStorage`. Verificado por inspección de código en cada revisión de bloque, sin mecanismo de persistencia del lado del cliente en ningún bloque. |
| NFR-02 (paginación compartida) | Block 5 |
| NFR-03 (mensaje 429 genérico) | Block 3 |
| NFR-04 (responsive ≥360px) | Strategy: Block 5 usa las utilidades responsive ya disponibles de Tailwind CSS + Angular Material MD3 (sin CSS a medida por breakpoint), verificado manualmente en VERIFY con el viewport del navegador en 360px. |

## Dependencies between blocks

Block 1 (backend) es independiente. Block 2 depende de Block 1 (necesita el endpoint para llamarlo).
Block 3 es independiente de 1/2 (toca el interceptor existente). Block 4 depende de Block 2 (el
guard consulta `SessionService`). Block 5 depende de Block 2 (el layout consulta el rol). Block 6
(E2E) depende de 1, 2, 3, 4 y 5 — ejercita el comportamiento integrado de todos.

Orden de implementación: 1 → 2 → 3 → 4 → 5 → 6.

## Block 1 — Backend: `GET /api/auth/whoami`

**Files**

- `backend/BingoCart.Api/Controllers/AuthController.cs` (nuevo) — controller mínimo, un solo
  endpoint.
- `backend/BingoCart.Api/Contracts/WhoAmIResponse.cs` (nuevo) — DTO `record WhoAmIResponse(string
  Rol, string Mail)`, mismo patrón que `PerfilOrganizadorResponse.cs` (excepción documentada al
  patrón de capas: sin lógica de negocio, se lee directo del claim ya verificado por el pipeline de
  auth).
- `backend/BingoCart.Api/Program.cs` (modificado) — política de rate limiting nueva
  `"auth-whoami"` (mitigación R-02 del threat model).
- `backend/tests/BingoCart.Api.Tests/Controllers/AuthControllerTests.cs` (nuevo).

**Logic**

`AuthController.WhoAmI()`: `[HttpGet("whoami")]`, `[Authorize]` (cualquier rol autenticado, sin
restricción de `Roles=`). Lee `User.FindFirstValue(ClaimTypes.Role)!` y
`User.FindFirstValue(ClaimTypes.Email)!` (el `!` es seguro porque `[Authorize]` garantiza que el
JWT, y por lo tanto ambos claims, existen — `JwtTokenService.GenerarToken` siempre los incluye para
ambos roles). Construye `WhoAmIResponse` con exactamente esos dos valores — **sin ningún otro
campo, sin serializar `User.Claims` genéricamente** (mitigación R-01, HIGH, del threat model).

**API contract**

- Método y ruta: `GET /api/auth/whoami`
- Request: sin body, sin query string
- Response 200 (`WhoAmIResponse`): `Rol` (string, `"Organizador"` o `"Comprador"`), `Mail` (string)
- Códigos de error: 401 (sin cookie de autenticación válida), 429 (límite excedido)
- Auth: `[Authorize]` sin restricción de rol. `[EnableRateLimiting("auth-whoami")]`

**Data model**

No crea ni modifica ningún esquema.

**Input validation**

Ninguna — el endpoint no acepta ningún parámetro.

**Error handling**

- Sin autenticación → 401 por el pipeline de `[Authorize]` existente, sin código propio.
- Límite de rate limiting excedido → 429, mismo mecanismo que las 9 políticas existentes.
- No hay ninguna rama de error propia del controller: si `[Authorize]` deja pasar la request, los
  claims existen por construcción (los emite `JwtTokenService` para ambos roles).

**Required tests**

- [ ] `AuthControllerTests.WhoAmI_ConSesionDeOrganizador_DevuelveRolYMail` — valida AC-01
- [ ] `AuthControllerTests.WhoAmI_ConSesionDeComprador_DevuelveRolYMail` — valida AC-01
- [ ] `AuthControllerTests.WhoAmI_LaRespuestaNoIncluyeNingunOtroCampo` — valida la mitigación R-01
      del threat model: parsea el JSON crudo y confirma que el set de propiedades es exactamente
      `{Rol, Mail}`, ni más ni menos (no `NameIdentifier`, no el JWT)
- [ ] `AuthControllerTests.WhoAmI_SinAutenticar_Devuelve401` — valida AC-02, sad path
- [ ] `AuthControllerTests.WhoAmI_SuperandoElLimiteDeSolicitudes_Devuelve429` — valida la mitigación
      R-02 del threat model

**Completion criterion**

El endpoint devuelve 200 con `{Rol, Mail}` para ambos roles, 401 sin sesión, 429 al superar el
límite; el test de forma del JSON confirma que no hay campos extra; los 5 tests en verde.

---

## Block 2 — Frontend: `SessionService`

**Files**

- `frontend/src/app/core/services/session.service.ts` (nuevo) — `providedIn: 'root'`, expone
  `rol$: Observable<'Organizador' | 'Comprador' | null>` y `mail$: Observable<string | null>`
  (o un único `sesion$: Observable<{rol, mail} | null>`, a decisión de implementación, siempre que
  ambos valores sean derivables), más un método público `resolverAsync(): Promise<void>` (o
  `Observable<void>`) que llama a `GET /api/auth/whoami` y actualiza el estado.
- `frontend/src/app/core/services/session.service.spec.ts` (nuevo).
- `frontend/src/app/app.module.ts` (modificado) — llama a `SessionService.resolverAsync()` en el
  bootstrap de la app (vía `APP_INITIALIZER` o en el constructor de `AppComponent`, a decisión de
  implementación — `APP_INITIALIZER` es preferible porque bloquea el render hasta resolver la
  sesión, evitando un parpadeo de layout anónimo→autenticado).
- `frontend/src/app/features/auth/services/auth.service.ts` (modificado) — se elimina
  `sesionExpiraEnUtcSubject`/`sesionExpiraEnUtc$` (confirmado sin consumidores en producción por el
  impact scan de PLAN); `SessionService` pasa a ser la única fuente de verdad de sesión.
- `frontend/src/app/features/auth/services/auth.service.spec.ts` (modificado) — se retira el test
  del subject eliminado.
- `frontend/src/app/features/auth/components/login-organizador/login-organizador.component.ts`
  (modificado) — tras un login exitoso, llama a `SessionService.resolverAsync()` antes de navegar,
  para que el rol se refleje inmediatamente (gap encontrado por el impact scan: la navegación es
  client-side, sin recarga completa, así que sin este llamado el rol quedaría desactualizado hasta
  el próximo refresh).
- `frontend/src/app/features/auth/components/login-organizador/login-organizador.component.spec.ts`
  (modificado).

**Logic**

`SessionService.resolverAsync()` llama a `GET /api/auth/whoami` con `withCredentials: true`. Si
200, actualiza el estado interno con `{rol, mail}`. Si 401 (sin sesión), actualiza el estado a
`null` — **no** es un error a propagar, es el estado legítimo "sin sesión". Se apoya en el
interceptor de Block 3 para no disparar una redirección en este caso puntual (mitigación R-03 del
threat model: whoami está excluido de la redirección automática en 401).

**Nunca** persiste el rol ni el mail en `localStorage`/`sessionStorage` (NFR-01) — el estado vive
únicamente en el `BehaviorSubject` de Angular, en memoria, y se reconstruye llamando a `whoami` en
cada arranque de la app.

**API contract**

No crea endpoints — consume `GET /api/auth/whoami` de Block 1.

**Input validation**

N/A — no hay input de usuario en este bloque.

**Error handling**

- 401 de `whoami` → estado de sesión `null`, sin error visible al usuario (es un estado esperado
  para un visitante anónimo).
- 429/5xx de `whoami` → el interceptor de Block 3 maneja el mensaje; `SessionService` deja el
  estado como estaba (no lo pisa con `null` por un error transitorio, para no desloguear
  visualmente a alguien con sesión válida por una falla momentánea de red).
- **El factory del `APP_INITIALIZER` (hallazgo de `daw-arch-auditor`) atrapa cualquier error de
  `resolverAsync()` — incluido un 5xx o un timeout de red durante el arranque — y resuelve la
  promesa igual, tratando ese fallo como "sesión no resuelta / anónima".** Es la primera vez que el
  proyecto bloquea el montaje completo de la app detrás de un round-trip de red (no hay precedente
  de `APP_INITIALIZER` en el frontend hasta este bloque — ver ADR-004); dejar la promesa sin atrapar
  dejaría la app entera sin montar ante cualquier falla transitoria, no solo la ruta protegida.

**Required tests**

- [ ] `SessionServiceTests.ResolverAsync_ConSesionDeOrganizador_ExponeElRolYElMail` — valida AC-03
- [ ] `SessionServiceTests.ResolverAsync_Sin401_ExponeSesionNula` — sad path, sesión anónima
- [ ] `SessionServiceTests.ResolverAsync_Con429o5xx_NoPisaUnaSesionYaResuelta` — sad path, no
      deslogea por un error transitorio
- [ ] `LoginOrganizadorComponentTests.OnSubmit_ConLoginExitoso_DisparaResolverAsyncAntesDeNavegar` —
      confirma el gap cerrado por el impact scan

**Completion criterion**

`SessionService` expone el rol y el mail correctos tras `resolverAsync()` para ambos roles y para
sesión anónima; `AuthService.sesionExpiraEnUtc$` ya no existe en el código; el login de organizador
dispara la resolución antes de navegar; los 4 tests en verde.

---

## Block 3 — Frontend: interceptor extendido (401/403/429)

**Files**

- `frontend/src/app/core/interceptors/http-error.interceptor.ts` (modificado) — agrega manejo de
  401, 403 y 429 al manejo ya existente de 5xx/red.
- `frontend/src/app/core/interceptors/http-error.interceptor.spec.ts` (modificado).

**Logic**

- 401: si la URL de la request **no** es `/api/auth/whoami`, `/api/organizadores/login` ni
  `/api/compradores/login` (mitigación R-03 del threat model, evita el loop de redirección),
  redirige a la pantalla de login correspondiente al rol esperado por la ruta actual (derivado del
  prefijo de la ruta activa: `/organizador/*` → login de organizador, `/comprador/*` o `/checkout/*`
  → login de comprador), preservando la URL de destino como query param o estado de navegación para
  volver tras autenticarse.
- 403: muestra un mensaje de "no tenés permiso para esta acción", sin redirigir.
- 429: muestra un mensaje genérico de límite excedido, **sin** sugerir un tiempo de espera (NFR-03
  — el backend no expone `Retry-After` en ninguna de sus 10 políticas, incluida la nueva
  `"auth-whoami"` de Block 1).
- 5xx y errores de red: comportamiento ya existente, sin cambios.

**Error handling**

Este bloque ES el manejo de errores centralizado — no aplica una capa adicional sobre sí mismo.

**Required tests**

- [ ] `HttpErrorInterceptorTests.Con401EnUnaLlamadaProtegida_RedirigeALaPantallaDeLoginCorrespondiente`
      — valida AC-06
- [ ] `HttpErrorInterceptorTests.Con401EnWhoamiOEnLogin_NoRedirige` — valida la mitigación R-03,
      sad path del loop de redirección
- [ ] `HttpErrorInterceptorTests.Con403_MuestraMensajeDePermisoDenegadoSinRedirigir` — valida AC-07
- [ ] `HttpErrorInterceptorTests.Con429_MuestraMensajeGenericoSinTiempoDeEspera` — valida AC-08 y
      NFR-03

**Completion criterion**

Los 4 tests en verde; ninguna ruta excluida (`whoami`, los dos `login`) dispara una redirección en
401; el mensaje de 429 no incluye ningún número de segundos ni cuenta regresiva.

---

## Block 4 — Frontend: guards de rol

**Files**

- `frontend/src/app/core/guards/organizador.guard.ts` (nuevo) — `CanActivateFn` que consulta
  `SessionService.rol$` (tomando el primer valor ya resuelto, dado que `APP_INITIALIZER` de Block 2
  garantiza que la resolución ya corrió antes de que el router evalúe cualquier guard).
- `frontend/src/app/core/guards/organizador.guard.spec.ts` (nuevo).
- `frontend/src/app/core/guards/comprador.guard.ts` (nuevo) — mismo patrón, rol `"Comprador"`.
- `frontend/src/app/core/guards/comprador.guard.spec.ts` (nuevo).

**Logic**

Cada guard es una función pura sobre el estado ya resuelto de `SessionService` (nunca dispara una
llamada HTTP propia — esa responsabilidad es exclusiva de `SessionService`/`APP_INITIALIZER`). Si
el rol no coincide con el exigido por la ruta, redirige a login (mismo mecanismo que el 401 del
interceptor de Block 3, preservando la ruta de destino), **sin** ejecutar ninguna llamada a la API
protegida de ese rol — el guard corta la navegación antes de que el componente de la ruta se
monte.

**Declaración explícita de la trust boundary (mitigación R-04 del threat model):** un comentario
XML doc en cada guard deja escrito que esta verificación es exclusivamente de UX — la autorización
real la hace cada endpoint del backend vía `[Authorize(Roles=...)]`, y un guard nunca sustituye
esa verificación.

**Input validation**

N/A.

**Error handling**

Si `SessionService` todavía no resolvió el rol (caso teóricamente imposible dado el
`APP_INITIALIZER` de Block 2, pero defendido igual), el guard trata ese estado como "sin sesión" y
redirige a login — nunca deja pasar la navegación por duda.

**Required tests**

- [ ] `OrganizadorGuardTests.ConRolOrganizador_PermiteLaNavegacion`
- [ ] `OrganizadorGuardTests.ConRolComprador_RedirigeALoginSinLlamarALaApi` — valida AC-09, sad
      path
- [ ] `OrganizadorGuardTests.SinSesion_RedirigeALogin` — valida AC-05, sad path
- [ ] `CompradorGuardTests.ConRolComprador_PermiteLaNavegacion`
- [ ] `CompradorGuardTests.ConRolOrganizador_RedirigeALoginSinLlamarALaApi` — valida AC-09, sad path
- [ ] `CompradorGuardTests.SinSesion_RedirigeALogin` — valida AC-05, sad path

**Completion criterion**

Los 6 tests en verde; ningún guard dispara una llamada HTTP propia; ambos guards redirigen sin
excepción cuando el rol no corresponde.

---

## Block 5 — Frontend: layout base y paginación compartida

**Files**

- `frontend/src/app/app.component.ts` (modificado) — pasa a ser shell puro: toolbar + navegación
  condicionada por `SessionService.rol$` + `<router-outlet>`. Se retira el contenido de "home"
  hardcodeado.
- `frontend/src/app/app.component.html` (modificado) — ídem.
- `frontend/src/app/app.component.spec.ts` (modificado).
- `frontend/src/app/features/home/home.component.ts` (nuevo) — contiene el contenido que hoy vive
  hardcodeado en `AppComponent` (card con link a `/auth/registro`).
- `frontend/src/app/features/home/home.component.html` (nuevo).
- `frontend/src/app/features/home/home.component.spec.ts` (nuevo).
- `frontend/src/app/app-routing.module.ts` (modificado) — agrega la ruta `{ path: '', component:
  HomeComponent }`.
- `frontend/src/app/shared/shared.module.ts` (nuevo) — primer módulo de `shared/` del proyecto.
- `frontend/src/app/shared/components/paginacion/paginacion.component.ts` (nuevo) — `@Input()
  total`, `@Input() page`, `@Input() pageSize`, `@Output() pageChange` — sin llamadas HTTP propias,
  puramente presentacional (consistente con la regla de `AGENTS.md`: la lógica de transformación de
  datos debe ser pura).
- `frontend/src/app/shared/components/paginacion/paginacion.component.html` (nuevo).
- `frontend/src/app/shared/components/paginacion/paginacion.component.spec.ts` (nuevo).

**Logic**

`AppComponent` muestra: sin sesión → nav con links a login/registro de organizador y de comprador;
sesión de organizador → nav con link a "mis bingos"; sesión de comprador → nav con link a "mis
cartones". Los links a rutas que todavía no existen (creadas en FEAT-010b/c/d/e) quedan
deshabilitados o no se muestran hasta que esos sub-tickets los agreguen — este bloque no inventa
rutas que no le corresponden. **El template consume `SessionService.rol$` vía el `async` pipe**
(`AGENTS.md`, "Code conventions": preferir `async` pipe en templates antes que `subscribe()`
manual), no una suscripción manual sin manejador de error.

El componente de paginación es genérico: recibe el estado (`total`, `page`, `pageSize`) y emite un
evento cuando el usuario cambia de página, sin saber nada del recurso paginado — FEAT-010b/c/e lo
van a consumir pasándole sus propios datos.

**Input validation**

N/A para el layout. El componente de paginación valida que `page` esté entre 1 y
`Math.ceil(total/pageSize)`, sin permitir navegar fuera de rango.

**Error handling**

N/A — sin llamadas a la API en este bloque.

**Required tests**

- [ ] `AppComponentTests.SinSesion_MuestraLaNavAnonima` — valida AC-03
- [ ] `AppComponentTests.ConSesionDeOrganizador_MuestraLaNavDeOrganizador` — valida AC-03, AC-04
- [ ] `AppComponentTests.ConSesionDeComprador_MuestraLaNavDeComprador` — valida AC-03, AC-04
- [ ] `HomeComponentTests.SeRenderiza_MuestraElContenidoDeHomeYaExistente`
- [ ] `PaginacionComponentTests.ConPageMenorAlTotal_EmitePageChangeAlAvanzar`
- [ ] `PaginacionComponentTests.EnLaUltimaPagina_NoPermiteAvanzarMasAlla` — sad path

**Completion criterion**

Los 6 tests en verde; `AppComponent` no contiene ningún contenido de negocio hardcodeado; el
componente de paginación no depende de ningún recurso específico.

---

## Block 6 — E2E: sesión y guards contra navegador real

**Files**

- `backend/tests/BingoCart.E2E.Tests/SesionYGuardsE2ETests.cs` (nuevo) — mismo patrón Playwright
  .NET que `LoginOrganizadorE2ETests.cs`/`RegistroOrganizadorE2ETests.cs`.

**Logic**

Cubre los comportamientos que un test unitario de Angular (`TestBed`) no puede verificar porque
dependen de un navegador real, una cookie `httpOnly` real, y una recarga de página real: AC-04
(refresh restaura la sesión), AC-05 (refresh sin cookie válida redirige a login preservando la
ruta), AC-09 (guard redirige sin llamar a la API del rol equivocado, verificado inspeccionando que
no hay una request de red a esa API en el log de Playwright).

**Error handling**

- Cookie ausente o expirada en una ruta protegida → el test confirma la redirección a login con la
  ruta de destino preservada (query param o estado de navegación), no un error sin manejar en
  consola del navegador.
- Rol equivocado en una ruta protegida → el test confirma que Playwright no registra ninguna
  request de red hacia el endpoint protegido de ese rol antes de la redirección — el guard corta la
  navegación, no deja que el componente intente la llamada y falle con 403.

**Required tests**

- [ ] `SesionYGuardsE2ETests.TrasLoginDeOrganizadorYRefresh_MantieneLaSesionYElLayout` — valida AC-04
- [ ] `SesionYGuardsE2ETests.SinCookieEnUnaRutaProtegida_RedirigeALoginPreservandoLaRutaDeDestino`
      — valida AC-05, sad path
- [ ] `SesionYGuardsE2ETests.ConRolOrganizadorEnUnaRutaDeComprador_RedirigeSinLlamarALaApiDeComprador`
      — valida AC-09, sad path

**Completion criterion**

Los 3 tests E2E en verde contra la app real (`:8000`/`:8080`), corridos dos veces consecutivas para
descartar flakiness.

## Final verification

Los 6 bloques completos, 26 tests en total (5+4+4+6+6+3, sin contar los de detalle interno) en
verde; `dotnet build`/`ng build` sin warnings; `dotnet format`/`ng lint` limpios; el frontend
arranca, resuelve la sesión antes de renderizar cualquier ruta protegida, y ningún dato de sesión
queda en `localStorage`/`sessionStorage` (inspección manual del storage del navegador en VERIFY).
