import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

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
 * escribible desde JavaScript por diseño). La resolución de "hay sesión / de qué rol" ya no vive
 * acá: `SessionService` (spec FEAT-010a, Block 2) es la única fuente de verdad de sesión, resuelta
 * contra `GET /api/auth/whoami` — este service quedó sin el `BehaviorSubject` que tenía
 * (`sesionExpiraEnUtc$`), confirmado sin consumidores en producción por el impact scan de PLAN.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly loginUrl = `${environment.apiUrl}/api/organizadores/login`;

  constructor(private readonly http: HttpClient) {}

  login(request: LoginRequest): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(this.loginUrl, request, { withCredentials: true });
  }
}
