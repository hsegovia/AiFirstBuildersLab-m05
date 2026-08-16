// Entorno de desarrollo. Reemplazado por environment.prod.ts en builds de producción
// vía fileReplacements (angular.json). apiUrl apunta al puerto real de BingoCart.Api (Block 4).
export const environment = {
  production: false,
  apiUrl: 'http://localhost:8080',
} as const;
