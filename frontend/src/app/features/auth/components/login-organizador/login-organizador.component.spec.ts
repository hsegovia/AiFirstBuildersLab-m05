import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ReactiveFormsModule } from '@angular/forms';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { RouterTestingModule } from '@angular/router/testing';
import { of, throwError } from 'rxjs';

import { LoginOrganizadorComponent } from './login-organizador.component';
import { AuthService } from '../../services/auth.service';
import { LoginResponse } from '../../models/login-response.model';

describe('LoginOrganizadorComponent', () => {
  let component: LoginOrganizadorComponent;
  let fixture: ComponentFixture<LoginOrganizadorComponent>;
  let authServiceSpy: jasmine.SpyObj<AuthService>;
  let router: Router;

  const datosValidos = {
    mail: 'organizador@example.com',
    password: 'Abcdefg1!',
  };

  beforeEach(async () => {
    authServiceSpy = jasmine.createSpyObj('AuthService', ['login']);

    await TestBed.configureTestingModule({
      declarations: [LoginOrganizadorComponent],
      imports: [
        ReactiveFormsModule,
        NoopAnimationsModule,
        RouterTestingModule,
        MatCardModule,
        MatFormFieldModule,
        MatInputModule,
        MatButtonModule,
      ],
      providers: [{ provide: AuthService, useValue: authServiceSpy }],
    }).compileComponents();

    fixture = TestBed.createComponent(LoginOrganizadorComponent);
    component = fixture.componentInstance;
    router = TestBed.inject(Router);
    fixture.detectChanges();
  });

  it('el envío del formulario con datos válidos invoca a AuthService.login()', () => {
    const response: LoginResponse = { expiraEnUtc: '2026-08-17T15:00:00Z' };
    authServiceSpy.login.and.returnValue(of(response));
    spyOn(router, 'navigateByUrl');

    component.form.setValue(datosValidos);
    component.onSubmit();

    expect(authServiceSpy.login).toHaveBeenCalledWith(datosValidos);
  });

  it('en éxito, redirige a home', () => {
    const response: LoginResponse = { expiraEnUtc: '2026-08-17T15:00:00Z' };
    authServiceSpy.login.and.returnValue(of(response));
    spyOn(router, 'navigateByUrl');

    component.form.setValue(datosValidos);
    component.onSubmit();

    expect(router.navigateByUrl).toHaveBeenCalledWith('/');
  });

  it('onSubmit con formulario inválido no llama al service', () => {
    component.form.setValue({ mail: '', password: '' });

    component.onSubmit();

    expect(authServiceSpy.login).not.toHaveBeenCalled();
  });

  it('un 401 simulado muestra el mensaje de error genérico sin redirigir', () => {
    const httpError = new HttpErrorResponse({
      status: 401,
      error: { error: 'CredencialesInvalidas', message: 'Credenciales inválidas.' },
    });
    authServiceSpy.login.and.returnValue(throwError(() => httpError));
    spyOn(router, 'navigateByUrl');

    component.form.setValue(datosValidos);
    component.onSubmit();

    expect(component.errorGeneral).toBe('Credenciales inválidas.');
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });
});
