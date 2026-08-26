import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot } from '@angular/router';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';

import { compradorGuard } from './comprador.guard';
import { SessionService } from '../services/session.service';

describe('compradorGuard', () => {
  let sessionService: SessionService;
  let routerSpy: jasmine.SpyObj<Router>;
  let httpMock: HttpTestingController;

  const ejecutarGuard = () =>
    TestBed.runInInjectionContext(() =>
      compradorGuard(
        {} as ActivatedRouteSnapshot,
        { url: '/comprador/mis-cartones' } as RouterStateSnapshot,
      ),
    );

  beforeEach(() => {
    routerSpy = jasmine.createSpyObj<Router>('Router', ['navigate']);

    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [SessionService, { provide: Router, useValue: routerSpy }],
    });

    sessionService = TestBed.inject(SessionService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('ConRolComprador_PermiteLaNavegacion', async () => {
    const promesa = sessionService.resolverAsync();
    httpMock.expectOne(() => true).flush({ rol: 'Comprador', mail: 'comprador@example.com' });
    await promesa;

    const resultado = ejecutarGuard();

    expect(resultado).toBeTrue();
    expect(routerSpy.navigate).not.toHaveBeenCalled();
  });

  it('ConRolOrganizador_RedirigeALoginSinLlamarALaApi', async () => {
    const promesa = sessionService.resolverAsync();
    httpMock
      .expectOne(() => true)
      .flush({ rol: 'Organizador', mail: 'organizador@example.com' });
    await promesa;

    const resultado = ejecutarGuard();

    expect(resultado).toBeFalse();
    expect(routerSpy.navigate).toHaveBeenCalledWith(
      ['/auth/login-comprador'],
      jasmine.objectContaining({
        queryParams: jasmine.objectContaining({ returnUrl: '/comprador/mis-cartones' }),
      }),
    );
    httpMock.expectNone(() => true);
  });

  it('SinSesion_RedirigeALogin', () => {
    const resultado = ejecutarGuard();

    expect(resultado).toBeFalse();
    expect(routerSpy.navigate).toHaveBeenCalledWith(
      ['/auth/login-comprador'],
      jasmine.objectContaining({
        queryParams: jasmine.objectContaining({ returnUrl: '/comprador/mis-cartones' }),
      }),
    );
    httpMock.expectNone(() => true);
  });
});
