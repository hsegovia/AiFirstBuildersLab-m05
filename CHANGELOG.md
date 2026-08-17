# Changelog

Todos los cambios notables de este proyecto se documentan en este archivo.

El formato sigue [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/).

## [Unreleased]

### Added

- **FEAT-001b**: Login de organizador — `POST /api/organizadores/login` (mail+contraseña),
  emisión de JWT firmado HMAC-SHA256 (60 min) transportado en una cookie `httpOnly`/`Secure`/
  `SameSite=Strict` (nunca en el body de la respuesta ni en `localStorage`, decisión de threat
  modeling). Lockout de cuenta tras 5 intentos fallidos consecutivos (bloqueo de 5 min), mismo
  mensaje de error para mail inexistente/password incorrecta/cuenta bloqueada. Endpoint mínimo
  protegido `GET /api/organizadores/perfil` para verificar end-to-end el rechazo de JWT expirados.
  Formulario de login en el frontend, sin persistencia del token en el cliente.

- **FEAT-001a**: Registro de organizador — solución completa desde cero: solución .NET 8 por
  capas (Domain/Application/Infrastructure/Api), Angular 18 con NgModules, SQL Server dockerizado
  con TDE habilitado. Endpoint `POST /api/organizadores/registro` con validación de CUIT (dígito
  verificador), teléfono, password (política de ASP.NET Core Identity) y mail (unicidad),
  rate limiting por IP, y formulario reactivo con manejo de error por campo. Stack completo
  containerizado (`docker-compose up --build`) para pruebas manuales end-to-end.

### Changed

- **FEAT-002**: reordenamiento de directorios (ADR-002) — el backend .NET completo (4 proyectos de
  producción, 5 de test, `BingoCart.sln`) se movió de `src/`/`tests/` (raíz del repo) a `backend/`,
  sin subnivel `backend/src/` intermedio, para quedar simétrico con `frontend/`. `docker-compose.yml`
  y el `Dockerfile` de la Api se actualizaron al nuevo contexto de build (`./backend`), y
  `backend/.dockerignore` reemplaza al `.dockerignore` de raíz. Sin cambios de comportamiento:
  build, 43/43 tests y el stack contenedorizado end-to-end quedaron verificados sobre la nueva
  ubicación.

### Fixed

- **FIX-002**: la home (`http://localhost:8000`) no tenía ningún link hacia el formulario de
  registro de organizador — solo era alcanzable escribiendo `/auth/registro` a mano. Se agregó un
  botón visible en la página de inicio.
