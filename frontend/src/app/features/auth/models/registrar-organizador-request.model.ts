/**
 * Payload de registro de organizador (Block 6, spec FEAT-001a). Espeja el contrato del backend:
 * `POST /api/organizadores/registro` (Block 4).
 */
export interface RegistrarOrganizadorRequest {
  readonly nombreOrganizacion: string;
  readonly cuit: string;
  readonly mail: string;
  readonly telefono: string;
  readonly password: string;
}
