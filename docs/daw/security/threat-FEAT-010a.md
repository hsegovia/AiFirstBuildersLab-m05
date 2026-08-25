# Threat Model — FEAT-010a (Infraestructura transversal y resolución de rol)

| Field | Value |
|-------|-------|
| Ticket | FEAT-010a |
| Date | 2026-08-25 |
| Diseño evaluado | 6 bloques: `GET /api/auth/whoami` (backend) + `SessionService` + interceptor extendido (401/403/429) + guards de rol + `AppComponent` shell/`HomeComponent`/paginación compartida + E2E |

## Trust boundaries (F-TM-02)

1. **Navegador (JavaScript, visible/modificable por el usuario) ↔ Backend (JWT en cookie
   `httpOnly`/`Secure`/`SameSite=Strict`)** — boundary ya existente, sin cambios. `whoami` no
   cruza esta frontera de forma nueva: solo devuelve al propio usuario autenticado dos datos que ya
   le pertenecen (su rol, su mail).
2. **Guard de ruta de Angular (cliente, trivialmente evitable con las devtools) ↔ Autorización real
   del backend (`[Authorize(Roles=...)]`, servidor)** — boundary crítico a declarar explícitamente:
   los guards de FR-04 son UX/defensa en profundidad, **nunca** el mecanismo de autorización real.
   Cualquier endpoint que un guard "protege" en el cliente sigue exigiendo su propio
   `[Authorize(Roles=...)]` server-side, sin excepción.

## Componentes evaluados (STRIDE, F-TM-01)

### `AuthController.WhoAmI` (`GET /api/auth/whoami`, nuevo)

| STRIDE | Evaluación |
|---|---|
| Spoofing | Hereda la autenticación JWT ya existente (cookie `httpOnly`/`Secure`/`SameSite=Strict`). Sin mecanismo de auth nuevo. |
| Tampering | Endpoint de solo lectura, sin escritura. N/A. |
| Repudiation | Sin cambio de estado, nada que repudiar. |
| **Information Disclosure** | 🟠 **HIGH** si se implementa ingenuamente (ej. serializar todos los claims del `ClaimsPrincipal`, o incluir `NameIdentifier`). El DTO de respuesta debe ser exactamente `{Rol, Mail}` — **nunca** el JWT crudo, **nunca** `NameIdentifier`. |
| Denial of Service | 🟡 MEDIUM — es el único endpoint autenticado del proyecto sin ninguna política de rate limiting (las otras 9 rutas del proyecto sí tienen una). Es una llamada barata (sin acceso a base de datos), pero rompe el precedente de cobertura consistente y se va a llamar con más frecuencia que cualquier otro endpoint (cada arranque de la app + después de cada login, en los 5 sub-tickets de FEAT-010). |
| Elevation of Privilege | N/A — no otorga ningún permiso, es puramente informativo para la UI. |

**Mitigación de Information Disclosure (HIGH):** el DTO de respuesta (`WhoAmIResponse.cs`, en
`Api/Contracts/`, mismo patrón que `PerfilOrganizadorResponse.cs`) declara únicamente dos
propiedades (`Rol`, `Mail`), construidas leyendo `ClaimTypes.Role`/`ClaimTypes.Email` uno por uno —
nunca un `Select`/serialización genérica sobre `User.Claims`. Verificado con test dedicado que
confirma que el JSON de respuesta no contiene ningún otro campo.

**Mitigación de DoS (MEDIUM):** se agrega una política de rate limiting nueva, `"auth-whoami"` —
60 permits / 1 minuto, particionada por `ClaimTypes.NameIdentifier` (mismo mecanismo que las 9
políticas existentes) — generosa porque es la llamada más frecuente del frontend, pero acotada.

### `SessionService` (frontend, nuevo)

| STRIDE | Evaluación |
|---|---|
| Spoofing | El rol resuelto es solo un espejo del backend — no se usa para tomar decisiones de autorización real en ningún punto. |
| Information Disclosure | El mail queda en memoria del cliente (estado de Angular), nunca en `localStorage`/`sessionStorage` (NFR-01 del PRD). Riesgo bajo, mismo criterio ya aceptado para el resto del proyecto. |
| Elevation of Privilege | 🟢 LOW, con mitigación de diseño: el spec debe dejar explícito, con un comentario en el propio `SessionService` y en cada guard, que el rol resuelto es **exclusivamente para UI** (qué nav mostrar, a qué ruta redirigir) — nunca la base de una decisión de seguridad. Es la misma declaración que la trust boundary #2 de arriba, aplicada al código. |

### Interceptor extendido (401/403/429)

| STRIDE | Evaluación |
|---|---|
| Denial of Service | 🟡 MEDIUM — riesgo de **loop de redirección**: si `whoami` (u otra llamada disparada desde la propia pantalla de login) devuelve 401, y el interceptor redirige a login, y la pantalla de login vuelve a disparar una llamada que también 401, se puede entrar en un ciclo. |
| Information Disclosure | El mensaje mostrado en 403/429 debe ser genérico (ya exigido por NFR-03 del PRD para 429) — nunca el cuerpo crudo del error del backend, que podría incluir detalles internos en un 5xx. |

**Mitigación del loop de redirección (MEDIUM):** el interceptor excluye explícitamente de la
lógica de "401 → redirigir a login" las propias llamadas a `/api/auth/whoami`,
`/api/organizadores/login`, `/api/compradores/login` — un 401 en esas rutas puntuales se propaga
como error normal al componente que las llamó (que ya tiene su propio manejo, como
`LoginOrganizadorComponent` ya demuestra), no dispara una redirección adicional.

### Guards de rol (`OrganizadorGuard`, `CompradorGuard`)

| STRIDE | Evaluación |
|---|---|
| Elevation of Privilege | 🟢 LOW — cubierto por la trust boundary #2 y la mitigación de `SessionService` arriba: el guard es UX, la autorización real vive en cada endpoint (`[Authorize(Roles=...)]`), sin excepción. |

### `AppComponent` (shell)/`HomeComponent`/paginación compartida

Sin superficie de ataque nueva — contenido estático, sin input de usuario, sin llamadas a la API
nuevas.

### Tests E2E (Playwright .NET)

Sin superficie de ataque en producción — son tests, no código que se despliega. El único cuidado es
no commitear credenciales reales, ya cubierto por el patrón existente en
`LoginOrganizadorE2ETests.cs`/`RegistroOrganizadorE2ETests.cs` (mails/passwords sintéticos
generados por test).

## Clasificación de datos sensibles (F-TM-05)

| Dato | Clasificación | Dónde aparece en este diseño |
|---|---|---|
| Mail del usuario | PII | Respuesta de `whoami` — ya expuesto por endpoints existentes (registro, login, perfil), sin exposición nueva. |
| Rol (Organizador/Comprador) | No sensible | Respuesta de `whoami` — el propio usuario ya conoce su rol. |
| JWT completo | Credencial | **Nunca** sale de la cookie `httpOnly` — `whoami` no lo expone, confirmado por la mitigación de Information Disclosure arriba. |

## Cifrado (F-TM-07)

Sin cambios: todo el tráfico ya viaja sobre HTTPS (infraestructura existente), la cookie ya es
`Secure`. `whoami` no introduce ningún dato que requiera cifrado adicional en reposo (no persiste
nada nuevo).

## Riesgos — resumen

| Riesgo | STRIDE | Likelihood | Impact | Mitigación |
|---|---|---|---|---|
| R-01 — `whoami` devuelve más datos de los debidos si se implementa ingenuamente | I | Medium | High | DTO explícito de 2 campos, sin serialización genérica de claims — verificado con test |
| R-02 — `whoami` sin rate limiting, único endpoint autenticado del proyecto sin política | D | Medium | Medium | Política nueva `"auth-whoami"`, 60/min por `NameIdentifier` |
| R-03 — Loop de redirección en 401 disparado por el propio flujo de login/whoami | D | Low | Medium | Interceptor excluye whoami/login de la redirección automática |
| R-04 — Guard de cliente confundido con autorización real | E | Low | High (si ocurriera) | Declarado explícitamente en código y en el spec: guard = UX, autorización = servidor, siempre |

Ningún riesgo queda sin mitigación folded al spec. No hay riesgos aceptados (no hace falta F-TM-04).

---

**Veredicto: PASSED.** Los 4 riesgos identificados (1 HIGH, 2 MEDIUM, 1 LOW-con-impacto-alto-si-ocurre)
tienen mitigación concreta, folded a los bloques 1, 2, 3 y 4 del spec respectivamente.
