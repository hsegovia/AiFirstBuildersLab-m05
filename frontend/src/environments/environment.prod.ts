// Entorno de producción. apiUrl coincide con el puerto real de BingoCart.Api expuesto por
// docker-compose (Block 7) — el spec no distingue un host distinto para prod en este ticket.
export const environment = {
  production: true,
  apiUrl: 'http://localhost:8080',
} as const;
