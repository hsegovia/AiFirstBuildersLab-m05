import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';

import { PaginacionComponent } from './components/paginacion/paginacion.component';

/**
 * Primer módulo de `shared/` del proyecto (spec FEAT-010a, Block 5). Agrupa componentes
 * presentacionales reutilizables entre features — hoy solo `PaginacionComponent`. Los feature
 * modules que necesiten paginación (FEAT-010b/c/e) lo importan directamente.
 */
@NgModule({
  declarations: [PaginacionComponent],
  imports: [CommonModule, MatButtonModule],
  exports: [PaginacionComponent],
})
export class SharedModule {}
