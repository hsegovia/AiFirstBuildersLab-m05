# Fix-plan FIX-002: Link de navegación a /auth/registro ausente en home

| Field | Value |
|-------|-------|
| Ticket | FIX-002 |
| Tier | FIX |
| RCA | docs/daw/specs/rca-FIX-002.md |
| Date | 2026-08-16 |
| Spec loops | 0 |

## Problem

`http://localhost:8000` muestra una card placeholder fija sin ningún link/botón hacia
`/auth/registro`. El formulario de registro de organizador (FEAT-001a, Block 6) solo es alcanzable
escribiendo la URL a mano — no hay forma de descubrirlo navegando desde la home.

## Root cause

Ver `docs/daw/specs/rca-FIX-002.md`: Block 5 dejó un placeholder estático en `app.component.html`
que nunca se conectó a la ruta `/auth/registro` que Block 6 implementó; ningún test (unitario ni
E2E) ejercita el flujo "desde la home hacia el formulario", porque los E2E navegan directo a la
ruta.

## Solution — steps

1. `frontend/src/app/app.module.ts` — agrega `MatButtonModule` (`@angular/material/button`) al
   array `imports` de `AppModule` (hoy solo tiene `MatToolbarModule` y `MatCardModule`).
2. `frontend/src/app/app.component.html` — agrega, dentro de la `mat-card` existente, un link:
   `<a mat-raised-button color="primary" routerLink="/auth/registro" data-testid="link-registro">Registrar organización</a>`.
   El `data-testid` sigue el mismo patrón que `registro-organizador.component.html` (todos sus
   elementos interactivos lo usan), detectado por el impact scan de PLAN.
3. `frontend/src/app/app.component.spec.ts` — agrega un test que confirme que el elemento con
   `data-testid="link-registro"` existe en el DOM renderizado y tiene el atributo `routerLink`
   apuntando a `/auth/registro`.

## Dependencies between steps

Ninguna — los 3 pasos son cambios en archivos distintos sin orden estricto entre sí, aunque el
paso 1 (import de `MatButtonModule`) debe existir para que `mat-raised-button` compile en el
template del paso 2.

## Error handling

N/A — cambio de UI puro (link estático), sin lógica de negocio ni manejo de errores nuevo.

## Tests

- [ ] **Regression test**: `AppComponentTests.MuestraUnLinkHaciaElFormularioDeRegistro` — busca
  `[data-testid="link-registro"]` en el DOM renderizado y confirma que existe con
  `routerLink="/auth/registro"`. Falla ANTES del fix (el elemento no existe), pasa DESPUÉS.
- [ ] Los 4 tests existentes de `app.component.spec.ts` (smoke test + los agregados en el
  corrective loop de FEAT-001a) siguen pasando sin regresión.

## Regression risk

Low — un solo archivo de template modificado (más un import de módulo), sin tocar lógica de
negocio, servicios, rutas existentes ni el backend. El único riesgo teórico es un typo en el path
del `routerLink`, cubierto por el propio regression test.

## Rollback plan

- Steps: trivial — revertir el commit de este fix (`git revert`). El cambio es puramente aditivo
  (un botón nuevo en un template), no modifica ni elimina nada existente.
- Indicators: si el link navega a una ruta incorrecta, o si el nuevo `data-testid` colisiona con
  otro elemento (no debería, es único en el árbol de componentes).
