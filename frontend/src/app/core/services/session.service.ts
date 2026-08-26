import { Injectable } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { BehaviorSubject, Observable, catchError, firstValueFrom, map, of, tap } from 'rxjs';

import { environment } from '../../../environments/environment';

/** Rol posible de una sesión activa (mismo vocabulario que el backend, `ClaimTypes.Role`). */
export type Rol = 'Organizador' | 'Comprador';

/** Sesión resuelta a partir de `GET /api/auth/whoami` (spec FEAT-010a, Block 2). */
export interface Sesion {
  readonly rol: Rol;
  readonly mail: string;
}

/** Cuerpo real (camelCase) de la respuesta 200 de `GET /api/auth/whoami` — ver Block 1. */
interface WhoAmIResponse {
  readonly rol: Rol;
  readonly mail: string;
}

/**
 * Única fuente de verdad de la sesión del frontend (spec FEAT-010a, Block 2). Reemplaza al
 * `BehaviorSubject` sin consumidores que tenía `AuthService`. El estado vive exclusivamente en
 * memoria (este `BehaviorSubject`) — nunca en `localStorage`/`sessionStorage` (NFR-01) — y se
 * reconstruye llamando a `whoami` en cada arranque de la app vía `APP_INITIALIZER`
 * (`app.module.ts`, ver ADR-004).
 */
@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly whoAmIUrl = `${environment.apiUrl}/api/auth/whoami`;

  private readonly sesionSubject = new BehaviorSubject<Sesion | null>(null);

  readonly sesion$: Observable<Sesion | null> = this.sesionSubject.asObservable();
  readonly rol$: Observable<Rol | null> = this.sesion$.pipe(map((sesion) => sesion?.rol ?? null));
  readonly mail$: Observable<string | null> = this.sesion$.pipe(
    map((sesion) => sesion?.mail ?? null),
  );

  constructor(private readonly http: HttpClient) {}

  /**
   * Llama a `GET /api/auth/whoami` y actualiza el estado interno. Nunca rechaza: un 401 es un
   * estado legítimo ("sesión anónima"), y un 429/5xx/error de red no pisa una sesión ya resuelta
   * — se ignora, dejando el último estado conocido, para no desloguear visualmente a alguien con
   * sesión válida por una falla transitoria.
   */
  resolverAsync(): Promise<void> {
    return firstValueFrom(
      this.http.get<WhoAmIResponse>(this.whoAmIUrl, { withCredentials: true }).pipe(
        tap((respuesta) =>
          this.sesionSubject.next({ rol: respuesta.rol, mail: respuesta.mail }),
        ),
        map(() => undefined),
        catchError((error: HttpErrorResponse) => {
          if (error.status === 401) {
            this.sesionSubject.next(null);
          }
          // 429/5xx/error de red: se deja el estado como estaba, ver doc del método.
          return of(undefined);
        }),
      ),
    );
  }
}
