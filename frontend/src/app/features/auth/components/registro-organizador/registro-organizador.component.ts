import { Component } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';

import { OrganizadorService } from '../../services/organizador.service';

/** Cuerpo de un error de negocio (400/409) devuelto por el endpoint de registro (Block 4). */
interface ErrorDeRegistro {
  readonly error: string;
  readonly message: string;
}

/**
 * Mapea cada código de error de negocio del contrato (Block 4) al nombre del control del
 * formulario reactivo donde debe mostrarse. `DatosInvalidos` no mapea a un campo puntual: puede
 * venir de cualquier DataAnnotation del request (`NombreOrganizacion`/`Mail`), así que se muestra
 * como error general del formulario.
 */
const CAMPO_POR_CODIGO_DE_ERROR: Readonly<Record<string, string | undefined>> = {
  CuitInvalido: 'cuit',
  TelefonoInvalido: 'telefono',
  PasswordInvalida: 'password',
  MailYaRegistrado: 'mail',
};

const PATRON_CUIT = /^\d{11}$/;
const PATRON_TELEFONO = /^[0-9+\-\s]+$/;
// Exige mayúscula, minúscula, dígito y carácter no alfanumérico — espeja la política por defecto
// de ASP.NET Core Identity (FR-04), reimplementada acá solo como validación de UX; la
// autoritativa sigue siendo el backend (Block 3).
const PATRON_PASSWORD_COMPLEJA = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^a-zA-Z0-9]).+$/;

@Component({
  selector: 'app-registro-organizador',
  templateUrl: './registro-organizador.component.html',
  styleUrls: ['./registro-organizador.component.scss'],
})
export class RegistroOrganizadorComponent {
  readonly form: FormGroup;

  registroExitoso = false;
  errorGeneral: string | null = null;
  enviando = false;

  constructor(
    private readonly formBuilder: FormBuilder,
    private readonly organizadorService: OrganizadorService,
  ) {
    this.form = this.formBuilder.group({
      nombreOrganizacion: ['', [Validators.required]],
      cuit: ['', [Validators.required, Validators.pattern(PATRON_CUIT)]],
      mail: ['', [Validators.required, Validators.email]],
      telefono: [
        '',
        [
          Validators.required,
          Validators.pattern(PATRON_TELEFONO),
          Validators.minLength(8),
          Validators.maxLength(20),
        ],
      ],
      password: [
        '',
        [
          Validators.required,
          Validators.minLength(8),
          Validators.pattern(PATRON_PASSWORD_COMPLEJA),
        ],
      ],
    });
  }

  onSubmit(): void {
    this.errorGeneral = null;
    this.registroExitoso = false;

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.enviando = true;

    this.organizadorService.registrar(this.form.getRawValue()).subscribe({
      next: () => {
        this.enviando = false;
        this.registroExitoso = true;
      },
      error: (error: HttpErrorResponse) => {
        this.enviando = false;
        this.manejarError(error);
      },
    });
  }

  private manejarError(error: HttpErrorResponse): void {
    const cuerpo = error.error as ErrorDeRegistro | null;

    if (!cuerpo?.error) {
      // Errores 5xx/red: el interceptor global (Block 5) ya los normaliza/loguea; acá no hay un
      // código de negocio por campo que mostrar.
      return;
    }

    const campo = CAMPO_POR_CODIGO_DE_ERROR[cuerpo.error];

    if (campo) {
      this.form.controls[campo].setErrors({ backend: cuerpo.message });
    } else {
      this.errorGeneral = cuerpo.message;
    }
  }
}
