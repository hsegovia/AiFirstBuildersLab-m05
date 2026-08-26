import { TestBed } from '@angular/core/testing';
import { RouterModule } from '@angular/router';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';

import { HomeComponent } from './home.component';

describe('HomeComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RouterModule.forRoot([]), MatCardModule, MatButtonModule, NoopAnimationsModule],
      declarations: [HomeComponent],
    }).compileComponents();
  });

  it('HomeComponentTests.SeRenderiza_MuestraElContenidoDeHomeYaExistente', () => {
    const fixture = TestBed.createComponent(HomeComponent);
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('mat-card-title')?.textContent).toContain('BingoCart');

    const link = compiled.querySelector('[data-testid="link-registro"]');
    expect(link).toBeTruthy();
    expect(link?.getAttribute('routerLink')).toBe('/auth/registro');
  });
});
