# SAST — FEAT-010a: Infraestructura transversal y resolución de rol (whoami)

**Fecha:** 2026-08-26  
**Ticket:** FEAT-010a  
**Archivos auditados:** 10 archivos de producción (backend + frontend introducidos en los 6 bloques)  
**Test runner:** manual (no existe `semgrep`/`bandit` configurado en el proyecto — revisión de patrones línea por línea, mismo método que sast-FEAT-009d.md)

---

## Secretos y configuración

- ✅ F-SAST-01 (secretos hardcodeados): sin API keys, tokens ni connection strings en archivos de producción del diff.
  - `SesionYGuardsE2ETests.cs` (test) usa `BingoCart_Dev2026!` como fallback de connection string **solo en tests**, con el patrón `Environment.GetEnvironmentVariable(...) ?? fallback` idéntico al de 5+ archivos de test preexistentes (`BingoRepositoryTests`, `MigracionCorrelativoTests`, `AppDbContextTests`, etc.) — no es superficie nueva ni se acerca a un archivo de producción. `.env` sigue en `.gitignore`.
  - `PasswordValida = "Abcdefg1!"` en el mismo archivo es una contraseña de setup de test E2E, no una credencial de usuario real — mismo patrón que `LoginOrganizadorE2ETests`/`RegistroOrganizadorE2ETests` preexistentes.

## Injection

- ✅ F-SAST-02 (SQL injection): `AuthController.WhoAmI` solo lee claims del `ClaimsPrincipal` ya verificado por el pipeline de JWT — sin ninguna consulta a la base de datos ni uso de ORM.
- N/A F-SAST-03 (command injection): sin entrada de usuario alcanzando exec/spawn en ninguno de los archivos de este ticket.
- N/A F-SAST-05 (path traversal): sin rutas de archivo derivadas de input de usuario.

## XSS y funciones inseguras

- ✅ F-SAST-06 (XSS): `app.component.html`, `home.component.html` y `paginacion.component.html` usan interpolación Angular (`{{ }}`) y directivas estructurales — sin `innerHTML` ni `[outerHTML]`. Angular escapa el output por defecto; no se usa `DomSanitizer.bypassSecurity*`.
- ✅ F-SAST-04/F-SAST-17: sin `eval()`, `new Function()` ni deserialización insegura en el diff.

## Logging y datos sensibles

- ✅ F-SAST-10 (logging de datos sensibles): los únicos `console.error` introducidos en este ticket son en `HttpErrorInterceptor` (mensajes genéricos: `'Error HTTP no manejado:'`, `'Acceso denegado:'`, `'Límite de solicitudes excedido:'`) — sin interpolación de datos de usuario, sin `request.body`, sin tokens. `AuthController.WhoAmI` no loguea nada. `SessionService.resolverAsync` no loguea nada.

## Otras categorías obligatorias

- N/A F-SAST-07 (SSRF): `SessionService` llama a `GET /api/auth/whoami` con una URL fija de `environment.apiUrl` (variable de entorno Angular, no derivada de input de usuario).
- N/A F-SAST-09 (debug mode en producción): sin cambios de `appsettings*.json` ni `environment.*.ts` de producción en este ticket.
- N/A F-SAST-11 (upload sin restricciones): sin endpoints de upload.
- ✅ F-SAST-12 (CSRF): `GET /api/auth/whoami` es de solo lectura; el único side effect del ticket (actualización del `BehaviorSubject` en memoria del frontend) no afecta estado persistente del servidor. Los guards y el interceptor son frontend-only. `SameSite=Strict` de la cookie `bingocart_auth` (preexistente desde FEAT-001a) cubre toda la superficie de CSRF.
- ✅ F-SAST-14 (validación de input incompleta): `whoami` no recibe ningún input del cliente — solo lee el JWT ya verificado por `AddJwtBearer`. Los guards y el interceptor no procesan input externo.
- ✅ F-SAST-15 (error handling que filtra internos): `AuthController.WhoAmI` devuelve solo `{rol, mail}` de los claims — sin stack trace, sin `NameIdentifier`, sin el JWT mismo. El interceptor devuelve mensajes fijos sin detalle de servidor.

## Dependencias

- ✅ F-SAST-13/16 (NuGet): `dotnet list package --vulnerable` → 0 paquetes vulnerables en los 9 proyectos de la solución. Ningún `.csproj` fue modificado por FEAT-010a.
- ⚠️ F-SAST-16 (npm): `npm audit` reporta 57 vulnerabilidades en `node_modules/webpack` (7 low, 17 moderate, 32 high, 1 critical — GHSA-8fgc-7cc6-rx7x y GHSA-38r7-794h-5758, ambas build-time SSRF en `webpack.buildHttp`). **Este hallazgo es preexistente y no fue introducido por FEAT-010a** (ninguna dependencia npm fue modificada en este ticket — confirmado por `git diff main..HEAD -- frontend/package.json` vacío). La superficie afectada es el toolchain de desarrollo (`webpack-dev-server`/`ng build`), no el bundle que se sirve al navegador. La corrección requiere `npm audit fix --force` con un upgrade que rompe `@angular-devkit/build-angular` a v21 — fuera del alcance de este ticket y ya presente en el entorno antes de iniciar FEAT-010a.

## Suppressions

Ninguna. El único hallazgo ⚠️ (webpack, npm, preexistente) no fue introducido por este ticket y no es suprimible como SAST de FEAT-010a — requiere un ticket dedicado de upgrades de dependencias.

---

**Total: 0 vulnerabilidades nuevas introducidas por FEAT-010a. 1 hallazgo preexistente de npm (webpack, no introducido por este ticket).**

**Veredicto: PASSED**
