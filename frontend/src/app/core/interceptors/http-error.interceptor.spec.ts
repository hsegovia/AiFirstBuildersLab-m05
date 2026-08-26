import { TestBed } from '@angular/core/testing';
import {
  HttpClient,
  HTTP_INTERCEPTORS,
  HttpErrorResponse,
} from '@angular/common/http';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { Router } from '@angular/router';

import { HttpErrorInterceptor } from './http-error.interceptor';

describe('HttpErrorInterceptor', () => {
  let httpClient: HttpClient;
  let httpTestingController: HttpTestingController;
  let routerSpy: jasmine.SpyObj<Router> & { url: string };

  beforeEach(() => {
    routerSpy = jasmine.createSpyObj<Router>('Router', ['navigate']) as jasmine.SpyObj<Router> & {
      url: string;
    };
    routerSpy.url = '/';

    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [
        { provide: HTTP_INTERCEPTORS, useClass: HttpErrorInterceptor, multi: true },
        { provide: Router, useValue: routerSpy },
      ],
    });

    httpClient = TestBed.inject(HttpClient);
    httpTestingController = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpTestingController.verify();
  });

  it('loguea y deja pasar un error de red (status 0) hacia abajo', () => {
    const consoleErrorSpy = spyOn(console, 'error');
    let errorRecibido: HttpErrorResponse | undefined;

    httpClient.get('/api/lo-que-sea').subscribe({
      next: () => fail('se esperaba un error'),
      error: (error: HttpErrorResponse) => (errorRecibido = error),
    });

    const request = httpTestingController.expectOne('/api/lo-que-sea');
    request.error(new ProgressEvent('error'), { status: 0, statusText: 'Unknown Error' });

    expect(errorRecibido?.status).toBe(0);
    expect(consoleErrorSpy).toHaveBeenCalledWith('Error HTTP no manejado:', jasmine.any(String));
  });

  it('loguea y deja pasar un error de servidor (5xx) hacia abajo', () => {
    const consoleErrorSpy = spyOn(console, 'error');
    let errorRecibido: HttpErrorResponse | undefined;

    httpClient.get('/api/lo-que-sea').subscribe({
      next: () => fail('se esperaba un error'),
      error: (error: HttpErrorResponse) => (errorRecibido = error),
    });

    const request = httpTestingController.expectOne('/api/lo-que-sea');
    request.flush('error interno', { status: 500, statusText: 'Internal Server Error' });

    expect(errorRecibido?.status).toBe(500);
    expect(consoleErrorSpy).toHaveBeenCalledWith('Error HTTP no manejado:', jasmine.any(String));
  });

  it('deja pasar un error de negocio (4xx) sin loguearlo', () => {
    const consoleErrorSpy = spyOn(console, 'error');
    let errorRecibido: HttpErrorResponse | undefined;

    httpClient.get('/api/lo-que-sea').subscribe({
      next: () => fail('se esperaba un error'),
      error: (error: HttpErrorResponse) => (errorRecibido = error),
    });

    const request = httpTestingController.expectOne('/api/lo-que-sea');
    request.flush(
      { error: 'DatosInvalidos', message: 'mensaje' },
      { status: 400, statusText: 'Bad Request' },
    );

    expect(errorRecibido?.status).toBe(400);
    expect(consoleErrorSpy).not.toHaveBeenCalled();
  });

  it('Con401EnUnaLlamadaProtegida_RedirigeALaPantallaDeLoginCorrespondiente', () => {
    // Escenario A: la ruta activa es de organizador → login de organizador.
    routerSpy.url = '/organizador/bingos';

    httpClient.get('/api/organizador/bingos').subscribe({ error: () => undefined });
    httpTestingController
      .expectOne('/api/organizador/bingos')
      .flush('no autenticado', { status: 401, statusText: 'Unauthorized' });

    expect(routerSpy.navigate).toHaveBeenCalledWith(
      ['/auth/login'],
      jasmine.objectContaining({
        queryParams: jasmine.objectContaining({ returnUrl: '/organizador/bingos' }),
      }),
    );

    routerSpy.navigate.calls.reset();

    // Escenario B: la ruta activa es de comprador → login de comprador.
    routerSpy.url = '/comprador/mis-cartones';

    httpClient.get('/api/comprador/cartones').subscribe({ error: () => undefined });
    httpTestingController
      .expectOne('/api/comprador/cartones')
      .flush('no autenticado', { status: 401, statusText: 'Unauthorized' });

    expect(routerSpy.navigate).toHaveBeenCalledWith(
      ['/auth/login-comprador'],
      jasmine.objectContaining({
        queryParams: jasmine.objectContaining({ returnUrl: '/comprador/mis-cartones' }),
      }),
    );
  });

  it('Con401EnWhoamiOEnLogin_NoRedirige', () => {
    // Control: una llamada NO excluida sí dispara la redirección — si esto no se cumple, el
    // resto del test sería trivial (navigate nunca se llamaría de todos modos).
    httpClient.get('/api/organizador/bingos').subscribe({ error: () => undefined });
    httpTestingController
      .expectOne('/api/organizador/bingos')
      .flush('no autenticado', { status: 401, statusText: 'Unauthorized' });

    expect(routerSpy.navigate).toHaveBeenCalledTimes(1);

    const urlsExcluidas = [
      '/api/auth/whoami',
      '/api/organizadores/login',
      '/api/compradores/login',
    ];

    urlsExcluidas.forEach((url) => {
      httpClient.get(url).subscribe({ error: () => undefined });
      httpTestingController
        .expectOne(url)
        .flush('no autenticado', { status: 401, statusText: 'Unauthorized' });
    });

    // El conteo de llamadas a navigate no debe cambiar: ninguna de las 3 URLs excluidas dispara
    // una redirección adicional (mitigación R-03 del threat model).
    expect(routerSpy.navigate).toHaveBeenCalledTimes(1);
  });

  it('Con403_MuestraMensajeDePermisoDenegadoSinRedirigir', () => {
    const consoleErrorSpy = spyOn(console, 'error');
    let errorRecibido: HttpErrorResponse | undefined;

    httpClient.get('/api/lo-que-sea').subscribe({
      next: () => fail('se esperaba un error'),
      error: (error: HttpErrorResponse) => (errorRecibido = error),
    });

    httpTestingController
      .expectOne('/api/lo-que-sea')
      .flush('sin permiso', { status: 403, statusText: 'Forbidden' });

    expect(errorRecibido?.status).toBe(403);
    expect(consoleErrorSpy).toHaveBeenCalled();
    const mensaje = consoleErrorSpy.calls.mostRecent().args.join(' ');
    expect(mensaje).toContain('permiso');
    expect(routerSpy.navigate).not.toHaveBeenCalled();
  });

  it('Con429_MuestraMensajeGenericoSinTiempoDeEspera', () => {
    const consoleErrorSpy = spyOn(console, 'error');
    let errorRecibido: HttpErrorResponse | undefined;

    httpClient.get('/api/lo-que-sea').subscribe({
      next: () => fail('se esperaba un error'),
      error: (error: HttpErrorResponse) => (errorRecibido = error),
    });

    httpTestingController
      .expectOne('/api/lo-que-sea')
      .flush('limite excedido', { status: 429, statusText: 'Too Many Requests' });

    expect(errorRecibido?.status).toBe(429);
    expect(consoleErrorSpy).toHaveBeenCalled();
    const mensaje = consoleErrorSpy.calls.mostRecent().args.join(' ');
    expect(mensaje).not.toMatch(/\d/);
    expect(mensaje.toLowerCase()).not.toContain('segundo');
    expect(mensaje.toLowerCase()).not.toContain('minuto');
  });
});
