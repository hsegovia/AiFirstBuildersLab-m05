import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { SessionService } from '../services/session.service';
import { loginPathPorRuta } from '../auth-navigation.util';

/**
 * Restringe la navegación a rutas de comprador a sesiones con rol `"Comprador"` (spec FEAT-010a,
 * Block 4). Lee el estado ya resuelto de `SessionService` de forma síncrona — nunca dispara una
 * llamada HTTP propia, esa responsabilidad es exclusiva de `SessionService`/`APP_INITIALIZER`
 * (Block 2). Si el rol no coincide (o la sesión todavía no se resolvió, caso teóricamente
 * imposible dado el `APP_INITIALIZER` pero defendido igual), redirige a la pantalla de login
 * correspondiente preservando la ruta de destino, reusando el mismo mecanismo que el manejo de
 * 401 del interceptor HTTP (Block 3, ver `auth-navigation.util.ts`).
 *
 * **Trust boundary (mitigación R-04 del threat model FEAT-010a):** esta verificación es
 * EXCLUSIVAMENTE de UX. La autorización real la hace cada endpoint del backend vía
 * `[Authorize(Roles=...)]`; este guard nunca sustituye esa verificación.
 */
export const compradorGuard: CanActivateFn = (_route, state) => {
  const sessionService = inject(SessionService);
  const router = inject(Router);

  if (sessionService.rolActual === 'Comprador') {
    return true;
  }

  void router.navigate([loginPathPorRuta(state.url)], {
    queryParams: { returnUrl: state.url },
  });
  return false;
};
