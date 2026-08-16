# RCA FIX-002: Link de navegación a /auth/registro ausente en home

| Field | Value |
|-------|-------|
| Ticket | FIX-002 |
| Date | 2026-08-16 |

## Root cause

`frontend/src/app/app.component.html` (Block 5 de FEAT-001a) quedó con una card placeholder
estática que nunca se reemplazó ni se conectó a la ruta `/auth/registro` cuando Block 6 implementó
el formulario real. `frontend/src/app/app-routing.module.ts` (Block 5) tampoco define ninguna ruta
raíz (`path: ''`) que redirija a `/auth/registro`, ni el placeholder incluye un `routerLink` hacia
ahí.

Cadena de eventos:
1. Block 5 creó `AppComponent` con contenido placeholder ("Infraestructura base del frontend
   lista…") como scaffolding temporal, sin la intención explícita en el spec de que fuera la UI
   final.
2. Block 6 agregó `AuthModule` con la ruta `registro` colgando de `auth/` (lazy-loaded), pero solo
   modificó `app-routing.module.ts` para agregar la entrada de rutas — nunca tocó
   `app.component.html`.
3. Los tests E2E (Playwright) navegan directamente a `http://localhost:8000/auth/registro`, así que
   nunca ejercitaron el flujo real de "un visitante entra a la home y busca cómo registrarse" —
   ningún test cubre la conexión entre ambos puntos.
4. Nadie identificó el gap hasta la revisión humana del PR en RELEASE, porque ni el PRD
   (`prd-FEAT-001a.md`) ni el spec (`spec-FEAT-001a.md`) especificaban explícitamente qué debía
   mostrar la ruta raíz.

## Affected component

`frontend/src/app/app.component.html` (frontend, Angular).

## Related PRD

`docs/daw/prd/prd-FEAT-001a.md` — sin gap: AC-01 especifica el comportamiento del envío del
formulario, no cómo se llega a él. No requiere modificación.

## Gap in the PRD

No.
