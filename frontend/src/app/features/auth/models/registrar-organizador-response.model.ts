/**
 * Respuesta exitosa (201) del registro de organizador (Block 6, spec FEAT-001a). Espeja
 * `RegistrarOrganizadorResponse` del backend (Block 3).
 */
export interface RegistrarOrganizadorResponse {
  readonly id: string;
  readonly nombreOrganizacion: string;
  readonly mail: string;
}
