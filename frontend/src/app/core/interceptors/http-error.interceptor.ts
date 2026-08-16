import { Injectable } from '@angular/core';
import {
  HttpErrorResponse,
  HttpEvent,
  HttpHandler,
  HttpInterceptor,
  HttpRequest,
} from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { catchError } from 'rxjs/operators';

/**
 * Normaliza errores HTTP no manejados explícitamente por un componente: 5xx y errores de
 * red/conectividad (status 0). NO reemplaza el manejo de error por campo que cada componente
 * hace sobre los códigos de negocio del contrato (400/409) — eso vive en cada feature (Block 6).
 */
@Injectable()
export class HttpErrorInterceptor implements HttpInterceptor {
  intercept(req: HttpRequest<unknown>, next: HttpHandler): Observable<HttpEvent<unknown>> {
    return next.handle(req).pipe(
      catchError((error: HttpErrorResponse) => {
        const isUnhandledServerOrNetworkError = error.status === 0 || error.status >= 500;

        if (isUnhandledServerOrNetworkError) {
          // Placeholder mínimo hasta que el proyecto defina un servicio de logging centralizado
          // (fuera de alcance de este bloque).
          console.error('Error HTTP no manejado:', error.message);
        }

        return throwError(() => error);
      }),
    );
  }
}
