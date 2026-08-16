# Fix FIX-001: Link de navegación a /auth/registro ausente en home

- **Bug**: `http://localhost:8000` muestra una card placeholder fija sin ningún link/botón hacia
  `/auth/registro`. El formulario de registro de organizador (FEAT-001a) solo es alcanzable
  escribiendo la URL a mano — no hay forma de descubrirlo navegando desde la home.
- **Change**: `frontend/src/app/app.component.html` — agrega un botón/link
  (`routerLink="/auth/registro"`, con `mat-raised-button` o similar de Angular Material) dentro de
  la card placeholder existente, visible en la home.
- **Regression test**: `AppComponentTests.MuestraUnLinkHaciaElFormularioDeRegistro` (Jasmine) —
  falla antes (no existe ningún elemento con `routerLink="/auth/registro"` en el DOM renderizado),
  pasa después.
- **Risk**: none — cambio de un solo archivo de template, sin lógica nueva, sin tocar rutas,
  servicios ni el backend.
