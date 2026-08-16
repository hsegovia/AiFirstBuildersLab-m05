# ADR-001: Riesgo aceptado — CVEs sin parche en Angular 18.x (`tar`, `@angular/core`)

| Field | Value |
|-------|-------|
| Date | 2026-08-16 |
| Ticket | FEAT-001a |
| Status | Accepted |

## Context

Durante el cierre de CODE de FEAT-001a (SAST, gate bloqueante), `npm audit` sobre `frontend/`
reportó 57 vulnerabilidades: 1 Critical y 32 High. Dos triages independientes con
`daw-sec-auditor` aislaron los hallazgos reales de severidad Critical/High:

1. **`tar` (Critical)** — CVE de DoS por descompresión + varias de path traversal
   (GHSA-23hp-3jrh-7fpw y relacionadas). Dependencia transitiva de `@angular/cli` (vía
   `pacote`/`node-gyp`/`cacache`), usada solo por npm/node-gyp al instalar paquetes.
   Verificado: 0 ocurrencias en `dist/frontend/browser/*.js` — nunca llega al bundle servido al
   navegador.
2. **`@angular/core` 18.2.14 (High)** — 5 CVEs de XSS/DOM-clobbering
   (GHSA-prjf-86w9-mfqv, GHSA-g93w-mfhg-p222, GHSA-jrmj-c5cx-3cw6, GHSA-rgjc-h3x7-9mwg,
   GHSA-jj27-h5hq-8x99). Esta sí es dependencia directa que viaja en el bundle de producción.
   Todas requieren features específicas (i18n/`$localize`, SVG dinámico, SSR/hydration, creación
   dinámica de componentes) que el código del proyecto **no usa en ningún punto** (verificado
   revisando cada template/componente existente).

`18.2.14` es la última versión publicada de la serie 18.x — no hay parche dentro del mismo major.
El catálogo de reglas de DAW (`.daw/rules/validation-rules.instructions.md` §4.1) clasifica
Critical/High como "no supresible... debe arreglarse", sin excepción para "sin fix disponible en
el mismo major". El único `fixAvailable` real es `@angular/core@22.1.2` / `@angular/cli@22.1.4`
— un salto de 4 versiones major, en conflicto directo con "Angular 18" declarado en el Stack de
`AGENTS.md`, y una migración de framework completa que no cabe dentro del alcance de ningún
ticket de feature puntual.

## Options considered

### Opción 1: Migrar Angular 18 → 22 dentro de este ticket
- **Pros:** elimina las 2 CVEs de raíz, deja el stack en la última versión soportada.
- **Cons:** 4 versiones major de diferencia — breaking changes no evaluados, posible
  incompatibilidad de NgModules con versiones futuras, riesgo de introducir regresiones en un
  ticket cuyo alcance real es un formulario de registro. Bloquearía indefinidamente el cierre de
  FEAT-001a por un problema que no es específico de esta feature.

### Opción 2: Aceptar el riesgo a nivel de proyecto, con seguimiento explícito
- **Pros:** no bloquea trabajo de producto por una migración de infraestructura que afecta a
  cualquier ticket futuro (no solo a este). La explotabilidad real hoy es nula/muy baja
  (verificada, no asumida): `tar` nunca llega al bundle, y ninguna feature vulnerable de
  `@angular/core` está en uso.
- **Cons:** el riesgo late en el bundle de producción hasta que se migre; requiere disciplina
  para no olvidar la migración.

## Decision

Se adopta la **Opción 2**, por decisión explícita del usuario. El riesgo se acepta a nivel de
**proyecto**, no solo de este ticket, porque afecta a cualquier feature que use `frontend/` hasta
que Angular se actualice.

## Consequences

- FEAT-001a cierra sin bloquear por este hallazgo. El reporte SAST (`docs/daw/security/sast-FEAT-001a.md`)
  documenta la supresión con los 7 campos de facto (aunque §4.4 solo define el mecanismo formal
  para Medium, se aplica la misma estructura por trazabilidad).
- **Pendiente:** crear un ticket FEATURE separado para planificar la migración de Angular 18 →
  una versión LTS sin estas CVEs, evaluando el impacto de breaking changes (NgModules,
  standalone components, etc.) antes de ejecutarla. No forma parte de FEAT-001a ni de ningún
  sub-ticket de registro de organizador.
- Cualquier feature futura que agregue i18n, SVG dinámico, SSR/hydration, o creación dinámica de
  componentes en `frontend/` debe re-evaluar este riesgo antes de implementarse, ya que esas son
  precisamente las superficies que las CVEs de `@angular/core` explotan.
- Revisión: re-evaluar en la próxima oportunidad de tocar `frontend/package.json`, o antes de
  6 meses desde esta fecha, lo que ocurra primero.
