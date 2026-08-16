import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';

import { RegistroOrganizadorComponent } from './components/registro-organizador/registro-organizador.component';

const routes: Routes = [{ path: 'registro', component: RegistroOrganizadorComponent }];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule],
})
export class AuthRoutingModule {}
