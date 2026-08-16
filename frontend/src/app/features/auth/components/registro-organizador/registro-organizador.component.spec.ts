import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ReactiveFormsModule } from '@angular/forms';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';

import { RegistroOrganizadorComponent } from './registro-organizador.component';
import { OrganizadorService } from '../../services/organizador.service';
import { RegistrarOrganizadorResponse } from '../../models/registrar-organizador-response.model';

describe('RegistroOrganizadorComponent', () => {
  let component: RegistroOrganizadorComponent;
  let fixture: ComponentFixture<RegistroOrganizadorComponent>;
  let organizadorServiceSpy: jasmine.SpyObj<OrganizadorService>;

  const datosValidos = {
    nombreOrganizacion: 'Club Social',
    cuit: '20304050607',
    mail: 'organizador@example.com',
    telefono: '+54 11 4444-5555',
    password: 'Abcdefg1!',
  };

  beforeEach(async () => {
    organizadorServiceSpy = jasmine.createSpyObj('OrganizadorService', ['registrar']);

    await TestBed.configureTestingModule({
      declarations: [RegistroOrganizadorComponent],
      imports: [
        ReactiveFormsModule,
        NoopAnimationsModule,
        MatCardModule,
        MatFormFieldModule,
        MatInputModule,
        MatButtonModule,
      ],
      providers: [{ provide: OrganizadorService, useValue: organizadorServiceSpy }],
    }).compileComponents();

    fixture = TestBed.createComponent(RegistroOrganizadorComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('el formulario es inválido si falta cualquier campo requerido', () => {
    component.form.setValue({
      nombreOrganizacion: '',
      cuit: datosValidos.cuit,
      mail: datosValidos.mail,
      telefono: datosValidos.telefono,
      password: datosValidos.password,
    });
    expect(component.form.valid).toBeFalse();

    component.form.setValue(datosValidos);
    expect(component.form.valid).toBeTrue();
  });

  it('onSubmit con formulario inválido no llama al service y marca todos los controles como touched', () => {
    component.form.setValue({
      ...datosValidos,
      nombreOrganizacion: '',
    });

    component.onSubmit();

    expect(organizadorServiceSpy.registrar).not.toHaveBeenCalled();
    for (const nombreControl of Object.keys(component.form.controls)) {
      expect(component.form.get(nombreControl)?.touched).toBeTrue();
    }
  });

  it('el validador de CUIT rechaza formato inválido en el cliente', () => {
    const cuitControl = component.form.controls['cuit'];

    cuitControl.setValue('123');
    expect(cuitControl.valid).toBeFalse();

    cuitControl.setValue('20304050607');
    expect(cuitControl.valid).toBeTrue();
  });

  it('en éxito (201) muestra el mensaje de éxito', () => {
    const response: RegistrarOrganizadorResponse = {
      id: '11111111-1111-1111-1111-111111111111',
      nombreOrganizacion: datosValidos.nombreOrganizacion,
      mail: datosValidos.mail,
    };
    organizadorServiceSpy.registrar.and.returnValue(of(response));

    component.form.setValue(datosValidos);
    component.onSubmit();

    expect(component.registroExitoso).toBeTrue();
  });

  const casosDeError: { codigo: string; campo: string | null }[] = [
    { codigo: 'CuitInvalido', campo: 'cuit' },
    { codigo: 'TelefonoInvalido', campo: 'telefono' },
    { codigo: 'PasswordInvalida', campo: 'password' },
    { codigo: 'MailYaRegistrado', campo: 'mail' },
    { codigo: 'DatosInvalidos', campo: null },
  ];

  for (const { codigo, campo } of casosDeError) {
    it(`muestra el mensaje de error correcto en el campo correcto ante ${codigo}`, () => {
      const httpError = new HttpErrorResponse({
        status: codigo === 'MailYaRegistrado' ? 409 : 400,
        error: { error: codigo, message: `mensaje de ${codigo}` },
      });
      organizadorServiceSpy.registrar.and.returnValue(throwError(() => httpError));

      component.form.setValue(datosValidos);
      component.onSubmit();

      if (campo) {
        expect(component.form.controls[campo].hasError('backend')).toBeTrue();
        expect(component.form.controls[campo].getError('backend')).toBe(`mensaje de ${codigo}`);
      } else {
        expect(component.errorGeneral).toBe(`mensaje de ${codigo}`);
      }
      expect(component.registroExitoso).toBeFalse();
    });
  }

  it('ante un error sin código de negocio (5xx/red) no toca el estado de error por campo', () => {
    const httpError = new HttpErrorResponse({ status: 500, error: null });
    organizadorServiceSpy.registrar.and.returnValue(throwError(() => httpError));

    component.form.setValue(datosValidos);
    component.onSubmit();

    expect(component.errorGeneral).toBeNull();
    for (const nombreControl of Object.keys(component.form.controls)) {
      expect(component.form.controls[nombreControl].hasError('backend')).toBeFalse();
    }
    expect(component.registroExitoso).toBeFalse();
    expect(component.enviando).toBeFalse();
  });
});
