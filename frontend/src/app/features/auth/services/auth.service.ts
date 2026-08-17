import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, tap } from 'rxjs';

import { environment } from '../../../../environments/environment';
import { LoginRequest } from '../models/login-request.model';
import { LoginResponse } from '../models/login-response.model';

/**
 * Único punto de acceso a `POST /api/organizadores/login` (spec FEAT-001b, Block 4). Separado de
 * `OrganizadorService` a propósito: autenticación es un concern transversal (va a crecer con
 * interceptor, guard y potencialmente refresh en tickets futuros) mientras que `OrganizadorService`
 * es específicamente el service de gestión de la cuenta del organizador.
 *
 * El JWT viaja exclusivamente en la cookie httpOnly `bingocart_auth` fijada por el backend — nunca
 * en el body de la respuesta, y por lo tanto nunca se persiste en `localStorage` (no es legible ni
 * escribible desde JavaScript por diseño). El `expiraEnUtc` recibido se guarda en memoria (un
 * `BehaviorSubject`) solo para que la UI sepa si "hay sesión" durante la vida de la pestaña, sin
 * persistencia entre recargas — limitación aceptada explícitamente en el spec (gap conocido, fuera
 * de alcance de este ticket resolverla con un guard/interceptor).
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly loginUrl = `${environment.apiUrl}/api/organizadores/login`;

  private readonly sesionExpiraEnUtcSubject = new BehaviorSubject<string | null>(null);
  readonly sesionExpiraEnUtc$: Observable<string | null> =
    this.sesionExpiraEnUtcSubject.asObservable();

  constructor(private readonly http: HttpClient) {}

  login(request: LoginRequest): Observable<LoginResponse> {
    return this.http
      .post<LoginResponse>(this.loginUrl, request, { withCredentials: true })
      .pipe(tap((response) => this.sesionExpiraEnUtcSubject.next(response.expiraEnUtc)));
  }
}
