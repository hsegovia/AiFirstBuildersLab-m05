import { Component } from '@angular/core';

import { SessionService } from './core/services/session.service';

/**
 * Shell puro de la app (spec FEAT-010a, Block 5): toolbar + navegación condicionada por rol +
 * `<router-outlet>`. No contiene contenido de negocio — el placeholder de "home" que vivía acá se
 * movió a `features/home/HomeComponent`. La navegación consume `SessionService.rol$` vía `async`
 * pipe en el template (AGENTS.md, "Code conventions"), sin `subscribe()` manual.
 */
@Component({
  selector: 'app-root',
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
})
export class AppComponent {
  readonly title = 'BingoCart';

  constructor(readonly sessionService: SessionService) {}
}
