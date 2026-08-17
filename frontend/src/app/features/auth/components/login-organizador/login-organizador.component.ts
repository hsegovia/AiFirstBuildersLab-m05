import { Component } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';

import { LoginRequest } from '../../models/login-request.model';
import { AuthService } from '../../services/auth.service';

/** Cuerpo de un error 401 (`ExceptionHandlingMiddleware`) devuelto por el endpoint de login (Block 2). */
interface ErrorDeLogin {
  readonly error: string;
  readonly message: string;
}

/**
 * Formulario de login de organizador (spec FEAT-001b, Block 4). Delega 100% la llamada HTTP a
 * `AuthService` — nunca `HttpClient` directo en el componente (AGENTS.md). Un 401 (credenciales
 * inválidas O cuenta bloqueada, indistinguibles a propósito — Block 2) se muestra como un único
 * mensaje genérico, sin distinguir causa. Los errores de red/5xx ya los maneja el interceptor HTTP
 * global (`http-error.interceptor.ts`), sin lógica adicional acá.
 */
@Component({
  selector: 'app-login-organizador',
  templateUrl: './login-organizador.component.html',
  styleUrls: ['./login-organizador.component.scss'],
})
export class LoginOrganizadorComponent {
  readonly form: FormGroup;

  errorGeneral: string | null = null;
  enviando = false;

  constructor(
    private readonly formBuilder: FormBuilder,
    private readonly authService: AuthService,
    private readonly router: Router,
  ) {
    this.form = this.formBuilder.group({
      mail: ['', [Validators.required, Validators.email]],
      password: ['', [Validators.required]],
    });
  }

  onSubmit(): void {
    this.errorGeneral = null;

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.enviando = true;
    const request: LoginRequest = this.form.getRawValue();

    this.authService.login(request).subscribe({
      next: () => {
        this.enviando = false;
        this.router.navigateByUrl('/');
      },
      error: (error: HttpErrorResponse) => {
        this.enviando = false;
        this.manejarError(error);
      },
    });
  }

  private manejarError(error: HttpErrorResponse): void {
    const cuerpo = error.error as ErrorDeLogin | null;

    if (error.status !== 401 || !cuerpo?.message) {
      // Errores 5xx/red: el interceptor global ya los normaliza/loguea; acá no hay un mensaje de
      // negocio que mostrar.
      return;
    }

    this.errorGeneral = cuerpo.message;
  }
}
