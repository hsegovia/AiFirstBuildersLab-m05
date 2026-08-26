import { TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { MatButtonModule } from '@angular/material/button';

import { PaginacionComponent } from './paginacion.component';

describe('PaginacionComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [MatButtonModule, NoopAnimationsModule],
      declarations: [PaginacionComponent],
    }).compileComponents();
  });

  it('PaginacionComponentTests.ConPageMenorAlTotal_EmitePageChangeAlAvanzar', () => {
    const fixture = TestBed.createComponent(PaginacionComponent);
    const component = fixture.componentInstance;
    component.total = 25;
    component.page = 1;
    component.pageSize = 10;
    fixture.detectChanges();

    let paginaEmitida: number | undefined;
    component.pageChange.subscribe((pagina) => (paginaEmitida = pagina));

    const compiled = fixture.nativeElement as HTMLElement;
    const botonSiguiente = compiled.querySelector<HTMLButtonElement>(
      '[data-testid="paginacion-siguiente"]',
    );
    botonSiguiente?.click();

    expect(paginaEmitida).toBe(2);
  });

  it('PaginacionComponentTests.EnLaUltimaPagina_NoPermiteAvanzarMasAlla', () => {
    const fixture = TestBed.createComponent(PaginacionComponent);
    const component = fixture.componentInstance;
    component.total = 25;
    component.page = 3;
    component.pageSize = 10;
    fixture.detectChanges();

    let emitido = false;
    component.pageChange.subscribe(() => (emitido = true));

    const compiled = fixture.nativeElement as HTMLElement;
    const botonSiguiente = compiled.querySelector<HTMLButtonElement>(
      '[data-testid="paginacion-siguiente"]',
    );

    expect(botonSiguiente?.disabled).toBeTrue();

    botonSiguiente?.click();

    expect(emitido).toBeFalse();
  });
});
