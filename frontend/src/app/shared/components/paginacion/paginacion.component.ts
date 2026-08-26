import { Component, EventEmitter, Input, Output } from '@angular/core';

/**
 * Paginación genérica y reutilizable (spec FEAT-010a, Block 5, NFR-02). Puramente presentacional:
 * no conoce el recurso que pagina, no hace llamadas HTTP propias — solo recibe `total`/`page`/
 * `pageSize` y emite `pageChange` cuando el usuario navega (AGENTS.md: la lógica de transformación
 * de datos debe ser pura). Los sub-tickets FEAT-010b/c/e la consumen pasándole sus propios datos.
 */
@Component({
  selector: 'app-paginacion',
  templateUrl: './paginacion.component.html',
})
export class PaginacionComponent {
  @Input() total = 0;
  @Input() page = 1;
  @Input() pageSize = 10;

  @Output() readonly pageChange = new EventEmitter<number>();

  /** Nunca menor a 1, incluso con `total` 0 (evita una "página 0 de 0"). */
  get totalPaginas(): number {
    return Math.max(1, Math.ceil(this.total / this.pageSize));
  }

  get esPrimeraPagina(): boolean {
    return this.page <= 1;
  }

  get esUltimaPagina(): boolean {
    return this.page >= this.totalPaginas;
  }

  irAPaginaAnterior(): void {
    this.irAPagina(this.page - 1);
  }

  irAPaginaSiguiente(): void {
    this.irAPagina(this.page + 1);
  }

  /** Nunca emite fuera del rango [1, totalPaginas] — el llamador nunca navega fuera de rango. */
  private irAPagina(pagina: number): void {
    if (pagina < 1 || pagina > this.totalPaginas) {
      return;
    }
    this.pageChange.emit(pagina);
  }
}
