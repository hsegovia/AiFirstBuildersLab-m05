# SAST FIX-002: Link de navegación a /auth/registro ausente en home

| Field | Value |
|-------|-------|
| Ticket | FIX-002 |
| Date | 2026-08-16 |
| Scope | Diff del fix (3 archivos: app.module.ts, app.component.html, app.component.spec.ts) |

## Secretos (F-SAST-01)
✅ Sin credenciales/tokens en el diff.

## Inyección (F-SAST-02/03/05)
✅ El `routerLink="/auth/registro"` es un literal estático en el template, no interpola ninguna
variable ni dato de usuario. Sin superficie de inyección.

## XSS y funciones inseguras (F-SAST-04/06/08/17)
✅ Sin `innerHTML`/`bypassSecurityTrust`. Texto del botón ("Registrar organización") es literal
estático, no interpolado.

## Dependencias (F-SAST-13/16)
✅ `MatButtonModule` ya es parte de `@angular/material`, ya declarado en `package.json` (Block 5 de
FEAT-001a) — sin dependencia nueva.

## Resultado

0 hallazgos. **PASSED** — `gates.sast` = `true`.
