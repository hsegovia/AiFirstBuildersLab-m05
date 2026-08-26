import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot } from '@angular/router';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';

import { organizadorGuard } from './organizador.guard';
import { SessionService } from '../services/session.service';

describe('organizadorGuard', () => {
  let sessionService: SessionService;
  let routerSpy: jasmine.SpyObj<Router>;
  let httpMock: HttpTestingController;

  const ejecutarGuard = () =>
    TestBed.runInInjectionContext(() =>
      organizadorGuard(
        {} as ActivatedRouteSnapshot,
        { url: '/organizador/bingos' } as RouterStateSnapshot,
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

  it('ConRolOrganizador_PermiteLaNavegacion', async () => {
    const promesa = sessionService.resolverAsync();
    httpMock
      .expectOne(() => true)
      .flush({ rol: 'Organizador', mail: 'organizador@example.com' });
    await promesa;

    const resultado = ejecutarGuard();

    expect(resultado).toBeTrue();
    expect(routerSpy.navigate).not.toHaveBeenCalled();
  });

  it('ConRolComprador_RedirigeALoginSinLlamarALaApi', async () => {
    const promesa = sessionService.resolverAsync();
    httpMock.expectOne(() => true).flush({ rol: 'Comprador', mail: 'comprador@example.com' });
    await promesa;

    const resultado = ejecutarGuard();

    expect(resultado).toBeFalse();
    expect(routerSpy.navigate).toHaveBeenCalledWith(
      ['/auth/login'],
      jasmine.objectContaining({
        queryParams: jasmine.objectContaining({ returnUrl: '/organizador/bingos' }),
      }),
    );
    // El guard no debe disparar ninguna llamada HTTP propia: la única request pendiente en el
    // mock es la de resolverAsync() de más arriba, ya consumida. httpMock.verify() (afterEach)
    // también falla el test si quedó alguna sin manejar.
    httpMock.expectNone(() => true);
  });

  it('SinSesion_RedirigeALogin', () => {
    const resultado = ejecutarGuard();

    expect(resultado).toBeFalse();
    expect(routerSpy.navigate).toHaveBeenCalledWith(
      ['/auth/login'],
      jasmine.objectContaining({
        queryParams: jasmine.objectContaining({ returnUrl: '/organizador/bingos' }),
      }),
    );
    httpMock.expectNone(() => true);
  });
});
