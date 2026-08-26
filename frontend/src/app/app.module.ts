import { APP_INITIALIZER, NgModule } from '@angular/core';
import { BrowserModule } from '@angular/platform-browser';
import { HttpClientModule } from '@angular/common/http';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';

import { AppRoutingModule } from './app-routing.module';
import { AppComponent } from './app.component';
import { CoreModule } from './core/core.module';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { SessionService } from './core/services/session.service';

/**
 * Factory del `APP_INITIALIZER` que resuelve la sesión antes del bootstrap (spec FEAT-010a,
 * Block 2 — ver ADR-004). **Atrapa cualquier error de `resolverAsync()` y resuelve la promesa
 * igual**: es la primera vez que el proyecto bloquea el montaje de la app detrás de un round-trip
 * de red, así que dejar la promesa rechazada colgaría el arranque de TODA la app (no solo de una
 * ruta protegida) ante un 5xx o un timeout transitorio durante el arranque.
 */
export function resolverSesionAlArrancar(sessionService: SessionService): () => Promise<void> {
  return () => sessionService.resolverAsync().catch(() => undefined);
}

@NgModule({
  declarations: [AppComponent],
  imports: [
    BrowserModule,
    HttpClientModule,
    CoreModule,
    MatToolbarModule,
    MatCardModule,
    MatButtonModule,
    AppRoutingModule,
  ],
  providers: [
    provideAnimationsAsync(),
    {
      provide: APP_INITIALIZER,
      useFactory: resolverSesionAlArrancar,
      deps: [SessionService],
      multi: true,
    },
  ],
  bootstrap: [AppComponent],
})
export class AppModule {}
