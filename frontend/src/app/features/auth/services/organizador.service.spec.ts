import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';

import { OrganizadorService } from './organizador.service';
import { RegistrarOrganizadorRequest } from '../models/registrar-organizador-request.model';
import { RegistrarOrganizadorResponse } from '../models/registrar-organizador-response.model';
import { environment } from '../../../../environments/environment';

describe('OrganizadorService', () => {
  let service: OrganizadorService;
  let httpMock: HttpTestingController;

  const request: RegistrarOrganizadorRequest = {
    nombreOrganizacion: 'Club Social',
    cuit: '20304050607',
    mail: 'organizador@example.com',
    telefono: '+54 11 4444-5555',
    password: 'Abcdefg1!',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [OrganizadorService],
    });

    service = TestBed.inject(OrganizadorService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('llama al endpoint correcto con el payload correcto', () => {
    const response: RegistrarOrganizadorResponse = {
      id: '11111111-1111-1111-1111-111111111111',
      nombreOrganizacion: request.nombreOrganizacion,
      mail: request.mail,
    };

    let received: RegistrarOrganizadorResponse | undefined;
    service.registrar(request).subscribe((r: RegistrarOrganizadorResponse) => (received = r));

    const req = httpMock.expectOne(`${environment.apiUrl}/api/organizadores/registro`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(request);

    req.flush(response, { status: 201, statusText: 'Created' });

    expect(received).toEqual(response);
  });
});
