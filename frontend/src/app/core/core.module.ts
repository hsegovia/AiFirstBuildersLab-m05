import { NgModule, Optional, SkipSelf } from '@angular/core';
import { HTTP_INTERCEPTORS } from '@angular/common/http';

import { HttpErrorInterceptor } from './interceptors/http-error.interceptor';

/**
 * Servicios/providers singleton de toda la app (interceptors, guards globales, etc.).
 * Se importa una única vez desde AppModule.
 */
@NgModule({
  providers: [{ provide: HTTP_INTERCEPTORS, useClass: HttpErrorInterceptor, multi: true }],
})
export class CoreModule {
  constructor(@Optional() @SkipSelf() parentModule: CoreModule) {
    if (parentModule) {
      throw new Error('CoreModule ya fue cargado. Impórtalo únicamente en AppModule.');
    }
  }
}
