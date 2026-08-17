/** Body de `POST /api/organizadores/login` (spec FEAT-001b, Block 4). */
export interface LoginRequest {
  readonly mail: string;
  readonly password: string;
}
