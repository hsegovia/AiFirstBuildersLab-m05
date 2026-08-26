import { Injectable } from '@angular/core';
import {
  HttpErrorResponse,
  HttpEvent,
  HttpHandler,
  HttpInterceptor,
  HttpRequest,
} from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, throwError } from 'rxjs';
import { catchError } from 'rxjs/operators';

import { loginPathPorRuta } from '../auth-navigation.util';

/**
 * Llamadas del propio flujo de autenticación excluidas de la redirección automática en 401
 * (mitigación R-03 del threat model FEAT-010a: evita el loop de redirección — un 401 en
 * cualquiera de estas 3 rutas se propaga como error normal al componente que la llamó, que ya
 * tiene su propio manejo: `SessionService.resolverAsync` para whoami,
 * `LoginOrganizadorComponent.manejarError` para el login de organizador).
 */
const URLS_EXCLUIDAS_DE_REDIRECCION_401 = [
  '/api/auth/whoami',
  '/api/organizadores/login',
  '/api/compradores/login',
];

const MENSAJE_403 = 'No tenés permiso para esta acción.';

// NFR-03: ninguna de las 10 políticas de rate limiting del backend expone `Retry-After`, así que
// el mensaje no debe sugerir ningún tiempo de espera.
const MENSAJE_429 = 'Alcanzaste el límite de solicitudes. Intentá de nuevo.';

/**
 * Normaliza errores HTTP no manejados explícitamente por un componente: 5xx y errores de
 * red/conectividad (status 0), y centraliza el manejo de 401/403/429 (spec FEAT-010a, Block 3).
 * NO reemplaza el manejo de error por campo que cada componente hace sobre los códigos de negocio
 * del contrato (400/409) — eso vive en cada feature (Block 6).
 */
@Injectable()
export class HttpErrorInterceptor implements HttpInterceptor {
  constructor(private readonly router: Router) {}

  intercept(req: HttpRequest<unknown>, next: HttpHandler): Observable<HttpEvent<unknown>> {
    return next.handle(req).pipe(
      catchError((error: HttpErrorResponse) => {
        const isUnhandledServerOrNetworkError = error.status === 0 || error.status >= 500;

        if (isUnhandledServerOrNetworkError) {
          // Placeholder mínimo hasta que el proyecto defina un servicio de logging centralizado
          // (fuera de alcance de este bloque).
          console.error('Error HTTP no manejado:', error.message);
        } else if (error.status === 401) {
          this.manejarNoAutenticado(req);
        } else if (error.status === 403) {
          console.error('Acceso denegado:', MENSAJE_403);
        } else if (error.status === 429) {
          console.error('Límite de solicitudes excedido:', MENSAJE_429);
        }

        return throwError(() => error);
      }),
    );
  }

  private manejarNoAutenticado(req: HttpRequest<unknown>): void {
    const esLlamadaExcluida = URLS_EXCLUIDAS_DE_REDIRECCION_401.some((url) =>
      req.url.includes(url),
    );

    if (esLlamadaExcluida) {
      return;
    }

    const rutaActual = this.router.url;
    const loginPath = loginPathPorRuta(rutaActual);

    void this.router.navigate([loginPath], { queryParams: { returnUrl: rutaActual } });
  }
}
