import { TestBed } from '@angular/core/testing';
import {
  HttpClient,
  HTTP_INTERCEPTORS,
  HttpErrorResponse,
} from '@angular/common/http';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';

import { HttpErrorInterceptor } from './http-error.interceptor';

describe('HttpErrorInterceptor', () => {
  let httpClient: HttpClient;
  let httpTestingController: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [
        { provide: HTTP_INTERCEPTORS, useClass: HttpErrorInterceptor, multi: true },
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
});
