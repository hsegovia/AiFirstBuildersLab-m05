import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';

import { SessionService } from './session.service';
import { environment } from '../../../environments/environment';

describe('SessionService', () => {
  let service: SessionService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [SessionService],
    });

    service = TestBed.inject(SessionService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('ResolverAsync_ConSesionDeOrganizador_ExponeElRolYElMail', async () => {
    const promesa = service.resolverAsync();

    const req = httpMock.expectOne(`${environment.apiUrl}/api/auth/whoami`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush({ rol: 'Organizador', mail: 'organizador@example.com' });

    await promesa;

    service.rol$.subscribe((valor) => expect(valor).toBe('Organizador'));
    service.mail$.subscribe((valor) => expect(valor).toBe('organizador@example.com'));
  });

  it('ResolverAsync_Sin401_ExponeSesionNula', async () => {
    const promesa = service.resolverAsync();

    const req = httpMock.expectOne(`${environment.apiUrl}/api/auth/whoami`);
    req.flush({ message: 'No autenticado' }, { status: 401, statusText: 'Unauthorized' });

    await promesa;

    service.rol$.subscribe((valor) => expect(valor).toBeNull());
  });

  it('ResolverAsync_Con429o5xx_NoPisaUnaSesionYaResuelta', async () => {
    const primeraPromesa = service.resolverAsync();
    const primeraReq = httpMock.expectOne(`${environment.apiUrl}/api/auth/whoami`);
    primeraReq.flush({ rol: 'Comprador', mail: 'comprador@example.com' });
    await primeraPromesa;

    const segundaPromesa = service.resolverAsync();
    const segundaReq = httpMock.expectOne(`${environment.apiUrl}/api/auth/whoami`);
    segundaReq.flush(
      { message: 'Límite excedido' },
      { status: 429, statusText: 'Too Many Requests' },
    );
    await segundaPromesa;

    service.rol$.subscribe((valor) => expect(valor).toBe('Comprador'));
    service.mail$.subscribe((valor) => expect(valor).toBe('comprador@example.com'));
  });
});
