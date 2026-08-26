import { TestBed } from '@angular/core/testing';
import { RouterModule } from '@angular/router';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { MatToolbarModule } from '@angular/material/toolbar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';

import { AppComponent } from './app.component';
import { SessionService } from './core/services/session.service';

describe('AppComponent', () => {
  let httpMock: HttpTestingController;
  let sessionService: SessionService;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [
        RouterModule.forRoot([]),
        HttpClientTestingModule,
        MatToolbarModule,
        NoopAnimationsModule,
      ],
      declarations: [AppComponent],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
    sessionService = TestBed.inject(SessionService);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(AppComponent);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it(`should have as title 'BingoCart'`, () => {
    const fixture = TestBed.createComponent(AppComponent);
    const app = fixture.componentInstance;
    expect(app.title).toEqual('BingoCart');
  });

  it('AppComponentTests.SinSesion_MuestraLaNavAnonima', () => {
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('[data-testid="nav-login-organizador"]')).toBeTruthy();
    expect(compiled.querySelector('[data-testid="nav-registro-organizador"]')).toBeTruthy();
    expect(compiled.querySelector('[data-testid="nav-mis-bingos"]')).toBeFalsy();
    expect(compiled.querySelector('[data-testid="nav-mis-cartones"]')).toBeFalsy();
  });

  it('AppComponentTests.ConSesionDeOrganizador_MuestraLaNavDeOrganizador', async () => {
    const promesa = sessionService.resolverAsync();
    httpMock.expectOne(() => true).flush({ rol: 'Organizador', mail: 'organizador@example.com' });
    await promesa;

    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('[data-testid="nav-mis-bingos"]')).toBeTruthy();
    expect(compiled.querySelector('[data-testid="nav-login-organizador"]')).toBeFalsy();
    expect(compiled.querySelector('[data-testid="nav-registro-organizador"]')).toBeFalsy();
    expect(compiled.querySelector('[data-testid="nav-mis-cartones"]')).toBeFalsy();
  });

  it('AppComponentTests.ConSesionDeComprador_MuestraLaNavDeComprador', async () => {
    const promesa = sessionService.resolverAsync();
    httpMock.expectOne(() => true).flush({ rol: 'Comprador', mail: 'comprador@example.com' });
    await promesa;

    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('[data-testid="nav-mis-cartones"]')).toBeTruthy();
    expect(compiled.querySelector('[data-testid="nav-login-organizador"]')).toBeFalsy();
    expect(compiled.querySelector('[data-testid="nav-registro-organizador"]')).toBeFalsy();
    expect(compiled.querySelector('[data-testid="nav-mis-bingos"]')).toBeFalsy();
  });
});
