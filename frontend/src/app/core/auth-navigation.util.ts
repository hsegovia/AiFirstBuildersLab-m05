/**
 * Mecanismo compartido para resolver "a qué pantalla de login redirigir" a partir del prefijo de
 * la ruta actual del router (spec FEAT-010a, Block 3 y Block 4). Extraído del interceptor HTTP
 * (Block 3) para que los guards de rol (Block 4) reutilicen exactamente la misma lógica en vez de
 * mantener una copia divergente — hallazgo de `daw-arch-auditor` en la revisión de Block 3.
 */

/** Pantalla de login de organizador — misma ruta que ya usa `AuthRoutingModule` hoy. */
export const LOGIN_ORGANIZADOR_PATH = '/auth/login';

/**
 * Pantalla de login de comprador. Al momento del Block 3 (FEAT-010a) todavía no existe un módulo
 * de rutas de comprador — lo agregan los sub-tickets FEAT-010c/d. Se asume esta ruta como destino
 * razonable, siguiendo la misma convención de `AuthRoutingModule` (`/auth/login`); si esos
 * sub-tickets definen una ruta distinta, este valor deberá actualizarse.
 */
export const LOGIN_COMPRADOR_PATH = '/auth/login-comprador';

/**
 * Deriva el rol esperado del prefijo de la ruta ACTUAL del router: `/organizador/*` →
 * Organizador; `/comprador/*` o `/checkout/*` → Comprador. Si el prefijo no es reconocible, se usa
 * Organizador como default razonable (decisión documentada en el spec, Block 3) — es el rol del
 * único flujo de auth que existe hoy en el proyecto.
 */
export function rolEsperadoPorRuta(rutaActual: string): 'Organizador' | 'Comprador' {
  if (rutaActual.startsWith('/comprador') || rutaActual.startsWith('/checkout')) {
    return 'Comprador';
  }

  return 'Organizador';
}

/** Resuelve la pantalla de login correspondiente al rol esperado por la ruta actual. */
export function loginPathPorRuta(rutaActual: string): string {
  return rolEsperadoPorRuta(rutaActual) === 'Comprador'
    ? LOGIN_COMPRADOR_PATH
    : LOGIN_ORGANIZADOR_PATH;
}
