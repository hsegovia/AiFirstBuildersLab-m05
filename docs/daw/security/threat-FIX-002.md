# Threat Model FIX-002: Link de navegación a /auth/registro ausente en home

| Field | Value |
|-------|-------|
| Ticket | FIX-002 |
| Date | 2026-08-16 |

## Componente analizado

`AppComponent` (`frontend/src/app/app.component.html`/`.ts`) — agrega un botón estático
(`routerLink="/auth/registro"`) dentro de la card ya existente. Ningún dato de usuario, ninguna
llamada HTTP nueva, ninguna lógica nueva: es navegación cliente pura hacia una ruta ya existente
(`AuthModule`, ya auditada en el threat model de FEAT-001a).

## Trust boundaries

Ninguno nuevo — el cambio vive enteramente dentro del navegador (SPA), sin cruzar ningún límite de
confianza distinto de los ya declarados en `docs/daw/security/threat-FEAT-001a.md` (TB-1: navegador
↔ API).

## STRIDE

| STRIDE | Análisis |
|---|---|
| Spoofing | N/A — sin autenticación ni identidad involucrada. |
| Tampering | N/A — el `routerLink` es un literal estático en el template, no interpola ningún dato de usuario ni de respuesta HTTP. |
| Repudiation | N/A — no es una acción que requiera trazabilidad. |
| Information Disclosure | N/A — no expone ningún dato nuevo; el botón solo navega a una ruta ya pública. |
| Denial of Service | N/A. |
| Elevation of Privilege | N/A. |

## Datos sensibles

Ninguno — el cambio no toca ningún dato personal ni credencial.

## Resultado

Sin riesgos identificados (0 Critical, 0 High, 0 Medium, 0 Low). Cambio de UI puro sin superficie
de ataque nueva.

**PASSED** — `gates.threat` = `true`.
