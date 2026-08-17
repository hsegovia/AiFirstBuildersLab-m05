/**
 * Respuesta 200 de `POST /api/organizadores/login` (spec FEAT-001b, Block 4). Sin campo `token`:
 * el JWT nunca llega al frontend en el body — viaja exclusivamente en la cookie httpOnly
 * `bingocart_auth` fijada por el backend (Block 2).
 */
export interface LoginResponse {
  readonly expiraEnUtc: string;
}
