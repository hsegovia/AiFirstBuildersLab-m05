import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';

import { HomeComponent } from './features/home/home.component';
import { organizadorGuard } from './core/guards/organizador.guard';
import { compradorGuard } from './core/guards/comprador.guard';

const routes: Routes = [
  { path: '', component: HomeComponent },
  {
    path: 'auth',
    loadChildren: () => import('./features/auth/auth.module').then((m) => m.AuthModule),
  },
  // --- TEMPORAL — spec FEAT-010a, Block 6 (E2E) --------------------------------------------
  // Hoy no existe ninguna ruta real de organizador/comprador en este router: las agregan los
  // sub-tickets FEAT-010b/c (organizador) y FEAT-010d/e (comprador). Sin al menos una ruta
  // protegida real, `SesionYGuardsE2ETests` no puede ejercitar `organizadorGuard`/`compradorGuard`
  // de punta a punta contra un navegador real. Estas dos entradas son placeholders MÍNIMOS
  // (reutilizan `HomeComponent`, puramente presentacional, sin llamadas HTTP propias) que solo
  // existen para que el guard tenga algo que proteger. Deliberadamente SIN el prefijo
  // `organizador/`/`comprador/` en el path: `rolEsperadoPorRuta` (auth-navigation.util.ts) solo
  // reconoce esos prefijos (y `checkout/`) para decidir a qué login redirigir, y hoy
  // `/auth/login-comprador` todavía no existe como ruta real (lo agrega FEAT-010d) — con un
  // prefijo `comprador/` el guard redirigiría a una ruta inexistente y el test no podría observar
  // una redirección real. Documentado explícitamente en el reporte del bloque como decisión de
  // alcance — DEBEN eliminarse (junto con los tests que las ejercitan en `SesionYGuardsE2ETests`)
  // en cuanto FEAT-010b/c/d/e agreguen las rutas reales.
  { path: 'placeholder-organizador-e2e-010a', component: HomeComponent, canActivate: [organizadorGuard] },
  { path: 'placeholder-comprador-e2e-010a', component: HomeComponent, canActivate: [compradorGuard] },
  // --- fin TEMPORAL --------------------------------------------------------------------------
];

@NgModule({
  imports: [RouterModule.forRoot(routes)],
  exports: [RouterModule],
})
export class AppRoutingModule {}
