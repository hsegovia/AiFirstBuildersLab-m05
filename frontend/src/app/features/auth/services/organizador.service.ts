import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

import { environment } from '../../../../environments/environment';
import { RegistrarOrganizadorRequest } from '../models/registrar-organizador-request.model';
import { RegistrarOrganizadorResponse } from '../models/registrar-organizador-response.model';

/**
 * Único punto de acceso a `POST /api/organizadores/registro` (Block 4 del backend) para el
 * feature `auth`. Los componentes de este feature nunca usan `HttpClient` directamente.
 */
@Injectable({ providedIn: 'root' })
export class OrganizadorService {
  private readonly registroUrl = `${environment.apiUrl}/api/organizadores/registro`;

  constructor(private readonly http: HttpClient) {}

  registrar(request: RegistrarOrganizadorRequest): Observable<RegistrarOrganizadorResponse> {
    return this.http.post<RegistrarOrganizadorResponse>(this.registroUrl, request);
  }
}
