import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';

import { RegistroOrganizadorComponent } from './components/registro-organizador/registro-organizador.component';
import { LoginOrganizadorComponent } from './components/login-organizador/login-organizador.component';

const routes: Routes = [
  { path: 'registro', component: RegistroOrganizadorComponent },
  { path: 'login', component: LoginOrganizadorComponent },
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule],
})
export class AuthRoutingModule {}
