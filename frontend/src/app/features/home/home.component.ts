import { Component } from '@angular/core';

/**
 * Contenido de "home" (spec FEAT-010a, Block 5): movido íntegro desde `AppComponent`, que pasa a
 * ser un shell puro sin contenido de negocio.
 */
@Component({
  selector: 'app-home',
  templateUrl: './home.component.html',
  styleUrl: './home.component.scss',
})
export class HomeComponent {
  readonly title = 'BingoCart';
}
