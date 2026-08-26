import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';

import { AuthService } from './auth.service';
import { LoginResponse } from '../models/login-response.model';
import { environment } from '../../../../environments/environment';

describe('AuthService', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [AuthService],
    });

    service = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('llama a POST /api/organizadores/login con el body correcto y withCredentials true', () => {
    const response: LoginResponse = { expiraEnUtc: '2026-08-17T15:00:00Z' };

    let received: LoginResponse | undefined;
    service.login({ mail: 'organizador@example.com', password: 'Abcdefg1!' }).subscribe((r) => (received = r));

    const req = httpMock.expectOne(`${environment.apiUrl}/api/organizadores/login`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      mail: 'organizador@example.com',
      password: 'Abcdefg1!',
    });
    expect(req.request.withCredentials).toBeTrue();

    req.flush(response);

    expect(received).toEqual(response);
  });
});
