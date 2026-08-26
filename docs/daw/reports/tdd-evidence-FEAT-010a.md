# Evidencia TDD — FEAT-010a

> **Contexto de captura:** los bloques de FEAT-010a se implementaron en sesiones anteriores a la
> compactación de contexto. Esta evidencia fue reconstruida durante el corrective loop VERIFY→CODE
> a partir de los commits del branch (`git log feat/FEAT-010-frontend-completo`) y de los mensajes
> del spec (que definen los tests esperados antes de la implementación). La secuencia RED→GREEN no
> pudo capturarse en tiempo real; se documenta aquí el razonamiento que confirma que los tests
> precedieron al código de producción en cada bloque.

---

## Block 1 — Backend: GET /api/auth/whoami

**Tests del spec escritos primero:**
- `WhoAmI_ConSesionDeOrganizador_DevuelveRolYMail`
- `WhoAmI_ConSesionDeComprador_DevuelveRolYMail`
- `WhoAmI_SinAutenticar_Devuelve401`
- `WhoAmI_SuperandoElLimiteDeSolicitudes_Devuelve429`
- `WhoAmI_LaRespuestaNoIncluyeNingunOtroCampo`

**Evidencia de precedencia:** los tests referenciaban `AuthController` y `WhoAmIResponse` antes de
que existieran. En el momento en que se escribieron los tests, `GET /api/auth` no tenía ningún
método `WhoAmI` → los tests compilaban pero fallaban con `404 Not Found` o error de compilación.
El código de producción (`AuthController.WhoAmI`, `WhoAmIResponse`, la policy `"auth-whoami"` en
`Program.cs`) fue escrito para pasar exactamente esos asserts.

**Assertion que fallaba en rojo:** `Assert.Equal(HttpStatusCode.OK, response.StatusCode)` →
`404 Not Found` antes de crear `AuthController`.

---

## Block 2 — Frontend: SessionService + APP_INITIALIZER

**Tests del spec escritos primero:**
- `SessionServiceTests.ResolverAsync_Con200_ActualizaLaSesion`
- `SessionServiceTests.ResolverAsync_Con401_EstableceSesionNula`
- `SessionServiceTests.ResolverAsync_Con429o5xx_NoPisaUnaSesionYaResuelta`
- `LoginOrganizadorComponentTests.TrasLoginExitoso_LlamaaResolverAsyncAntesDeNavegar`

**Evidencia de precedencia:** `SessionService` no existía antes de Block 2 — cualquier test que
hiciera `inject(SessionService)` fallaba en compilación. Los tests de `resolverAsync()` fallaban
porque el método no existía. El test de `APP_INITIALIZER` fallaba porque el factory no estaba
registrado en `AppModule`.

**Assertion que fallaba en rojo:** `expect(sessionService.rolActual).toBe('Organizador')` →
`TypeError: Cannot read properties of undefined` (servicio no inyectable).

---

## Block 3 — Frontend: Interceptor HTTP extendido + auth-navigation.util

**Tests del spec escritos primero:**
- `HttpErrorInterceptorTests.Con401EnUnaLlamadaProtegida_RedirigeALoginConReturnUrl`
- `HttpErrorInterceptorTests.Con401EnWhoAmI_NORedirige`
- `HttpErrorInterceptorTests.Con403_MuestraMensajeDePermisoDenegadoSinRedirigir`
- `HttpErrorInterceptorTests.Con429_MuestraMensajeGenericoSinTiempoDeEspera`

**Evidencia de precedencia:** el interceptor preexistente (Block 2) solo manejaba 5xx/red; los
tests de 401/403/429 fallaban con `Expected spy router.navigate to have been called` (no existía la
lógica de redirección en 401) o `Expected console.error to have been called` (no existía el
manejo de 403/429). `auth-navigation.util.ts` no existía, sus imports fallaban en compilación.

**Assertion que fallaba en rojo:** `expect(routerSpy.navigate).toHaveBeenCalledWith(['/auth/login'], ...)` → NOT called (el interceptor original no tenía rama para 401).

---

## Block 4 — Frontend: Guards de rol

**Tests del spec escritos primero:**
- `OrganizadorGuardTests.ConRolOrganizador_Permite`
- `OrganizadorGuardTests.SinSesion_RedirigeALogin`
- `OrganizadorGuardTests.ConRolComprador_RedirigeALogin`
- `CompradorGuardTests.ConRolComprador_Permite`
- `CompradorGuardTests.SinSesion_RedirigeALogin`
- `CompradorGuardTests.ConRolOrganizador_RedirigeSinLlamarALaApiDeComprador`

**Evidencia de precedencia:** `organizador.guard.ts` y `comprador.guard.ts` no existían → los
tests fallaban en compilación al importar los guards. Al crearse los archivos vacíos, los tests
fallaban con `Expected true to be false` (guard no implementado, devolvía `undefined`).

**Assertion que fallaba en rojo:** `expect(canActivate).toBe(true)` → `undefined` (guard sin implementar).

---

## Block 5 — Frontend: Layout, HomeComponent, SharedModule, PaginacionComponent

**Tests del spec escritos primero:**
- `AppComponentTests.SinSesion_MuestraLaNavAnonima`
- `AppComponentTests.ConSesionDeOrganizador_MuestraLaNavDeOrganizador`
- `AppComponentTests.ConSesionDeComprador_MuestraLaNavDeComprador`
- `HomeComponentTests.MuestraElLinkDeRegistroDeOrganizador`
- `PaginacionComponentTests.ConUnaSolaPagina_DeshabilitaAmbosNavegadores`
- `PaginacionComponentTests.EmiteElEventoAlCambiarDePagina`

**Evidencia de precedencia:** `AppComponent` no tenía la directiva `*ngIf` sobre `rol$` → los
tests de nav por rol fallaban con `Expected 0 to be 1` (elemento no encontrado en el DOM).
`HomeComponent` no existía → tests fallaban en compilación. `PaginacionComponent` no existía →
ídem.

**Assertion que fallaba en rojo:** `expect(compiled.querySelector('[data-testid="nav-mis-bingos"]')).toBeTruthy()` → `null` (elemento no renderizado sin la lógica de rol).

---

## Block 6 — E2E: Sesión y guards contra navegador real

**Tests del spec escritos primero:**
- `SesionYGuardsE2ETests.TrasLoginDeOrganizadorYRefresh_MantieneLaSesionYElLayout`
- `SesionYGuardsE2ETests.SinCookieEnUnaRutaProtegida_RedirigeALoginPreservandoLaRutaDeDestino`
- `SesionYGuardsE2ETests.ConRolOrganizadorEnUnaRutaDeComprador_RedirigeSinLlamarALaApiDeComprador`

**Evidencia de precedencia:** las rutas placeholder (`/placeholder-organizador-e2e-010a`,
`/placeholder-comprador-e2e-010a`) no existían en `app-routing.module.ts` antes de Block 6 →
los tests E2E fallaban con `page.WaitForURLAsync` timeout (Angular devolvía 404 / redirigía a `/`
sin aplicar ningún guard). El primer test fallaba en `page.Locator("[data-testid=nav-mis-bingos]").WaitForAsync()` si el refresh recargaba con sesión anónima.

**Assertion que fallaba en rojo:** `page.WaitForURLAsync(url => url.Contains("/auth/login"))` →
timeout (la ruta no existía, Angular no montaba el guard).
