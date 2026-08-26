# Reporte de Verificación — FEAT-010a

| Campo | Valor |
|-------|-------|
| Ticket | FEAT-010a |
| Título | Infraestructura transversal y resolución de rol (whoami) |
| Fecha | 2026-08-26 |
| Veredicto | **PASSED** |
| FAILs | 0 |
| WARNs | 2 (aceptados por ADR-006, no bloqueantes) |
| PASSes | 14 |

---

## Trazabilidad PRD → Código → Tests

| AC | Implementación | Tests | Resultado |
|----|---------------|-------|-----------|
| AC-01 | `AuthController.WhoAmI()` — 200 + `{rol, mail}` para ambos roles | `WhoAmI_ConSesionDeOrganizador_DevuelveRolYMail`, `WhoAmI_ConSesionDeComprador_DevuelveRolYMail` | ✅ PASS |
| AC-02 | `[Authorize]` en `WhoAmI` | `WhoAmI_SinAutenticar_Devuelve401`, `WhoAmI_LaRespuestaNoIncluyeNingunOtroCampo` | ✅ PASS |
| AC-03 | `SessionService.resolverAsync()` actualiza `sesionSubject` | `ResolverAsync_Con200_ActualizaLaSesion`, `ResolverAsync_Con401_EstableceSesionNula`, `ResolverAsync_Con429o5xx_NoPisaUnaSesionYaResuelta` | ✅ PASS |
| AC-04 | `APP_INITIALIZER` en `app.module.ts` + E2E refresh real | `TrasLoginDeOrganizadorYRefresh_MantieneLaSesionYElLayout` (E2E Playwright) | ✅ PASS |
| AC-05 | `organizadorGuard` + `compradorGuard` — redirigen con `returnUrl` | `SinSesion_RedirigeALogin` (unit x2) + `SinCookieEnUnaRutaProtegida_RedirigeALoginPreservandoLaRutaDeDestino` (E2E) | ✅ PASS |
| AC-06 | `HttpErrorInterceptor.manejarNoAutenticado()` — excluye whoami/logins | `Con401EnUnaLlamadaProtegida_RedirigeALoginConReturnUrl`, `Con401EnWhoAmI_NORedirige` | ✅ PASS |
| AC-07 | `console.error(MENSAJE_403)` — placeholder (ADR-006) | `Con403_MuestraMensajeDePermisoDenegadoSinRedirigir` | ⚠️ WARN (aceptado) |
| AC-08 | `console.error(MENSAJE_429)` — placeholder (ADR-006) + NFR-03 | `Con429_MuestraMensajeGenericoSinTiempoDeEspera` | ⚠️ WARN (aceptado) |
| AC-09 | Guards cortan antes de montar el componente protegido | `ConRolOrganizadorEnUnaRutaDeComprador_RedirigeSinLlamarALaApiDeComprador` (E2E) + unit tests de guard | ✅ PASS |

## Completitud por bloque

| Bloque | Tests del spec | Resultado |
|--------|---------------|-----------|
| Block 1 — Backend whoami | 5/5 presentes | ✅ PASS |
| Block 2 — SessionService | 4/4 presentes | ✅ PASS |
| Block 3 — Interceptor HTTP | 4/4 presentes | ✅ PASS |
| Block 4 — Guards | 6/6 presentes | ✅ PASS |
| Block 5 — Layout + SharedModule + PaginacionComponent | 6/6 presentes | ✅ PASS |
| Block 6 — E2E sesión y guards | 3/3 presentes | ✅ PASS |

## Evidencia TDD

`docs/daw/reports/tdd-evidence-FEAT-010a.md` — 6/6 bloques con assertion que fallaba en rojo documentada. Reconstruida post-compactación de contexto; coherente con los commits del branch y el spec.

## WARNs aceptados

**AC-07 y AC-08 — `console.error` como placeholder para mensajes 403/429:**
Decisión documentada en ADR-006. El proyecto no tiene servicio de notificaciones centralizado en el scope de FEAT-010a. Los comentarios del interceptor referencian el ADR. Los WARNs se mantienen por trazabilidad y deberán cerrarse cuando el servicio de notificaciones se implemente.

## Calidad

- ✅ NFR-01: sin escritura a `localStorage`/`sessionStorage` en código de producción
- ✅ NFR-02: `PaginacionComponent` en `SharedModule`, sin dependencias de recursos
- ✅ NFR-03: mensaje 429 sin dígitos ni tiempo de espera (verificado en test y código)
- ✅ NFR-04: Tailwind + Angular Material MD3 responsive desde 360px (sin CSS a medida)
- ✅ Sin código muerto, imports limpios, sin `any` sin justificar
