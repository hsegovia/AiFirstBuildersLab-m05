# Changelog

Todos los cambios notables de este proyecto se documentan en este archivo.

El formato sigue [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/).

## [Unreleased]

### Added

- **FEAT-008a**: Descubrimiento público de cartones — `GET /api/cartones/descubrimiento` (5
  cartones aleatorios de cualquier bingo activo) y `GET /api/cartones/organizador/{organizadorId}`
  (5 cartones aleatorios del bingo activo de un organizador), ambos públicos, sin autenticación.
  Selección aleatoria resuelta en SQL Server (`ORDER BY NEWID()` vía `FromSqlInterpolated`
  parametrizado — primer SQL crudo del proyecto), nunca cargando en memoria el conjunto completo
  de cartones candidatos. El directorio público (`GET /api/organizadores/directorio`, FEAT-005)
  ahora también expone el `Id` del organizador, necesario para conectar ambos flujos. Rate limiting
  de 60 requests/5 min por IP. Backend-only, sin pantalla de descubrimiento en el frontend todavía.

- **FEAT-007**: Edición y eliminación de bingo sin compras — `PUT /api/bingos/{id}` (edita nombre
  de evento, fecha de sorteo y costo por cartón) y `DELETE /api/bingos/{id}` (elimina el bingo y
  todos sus cartones, vía cascade de esquema) para el organizador dueño, siempre que el bingo no
  tenga compras registradas. Primera entidad mutable del dominio (`Bingo.Actualizar`) y primer 404
  del proyecto. Un organizador nunca puede editar ni eliminar un bingo ajeno: mismo tipo de error
  (404) para "no existe" y "es de otro organizador", sin distinguir los casos. Backend-only, sin
  pantalla de edición/eliminación en el frontend todavía.

- **FEAT-005**: Directorio público de organizadores — `GET /api/organizadores/directorio`
  (público, sin autenticación): cualquier visitante lista los organizadores con un bingo de sorteo
  futuro (nombre de la organización, nombre del evento, fecha de sorteo), paginado y ordenado por
  fecha de sorteo ascendente. La proyección nunca expone CUIT/mail/teléfono del organizador
  (verificado contra el body crudo de la respuesta, no solo el tipo deserializado). Rate limiting
  de 30 requests/5 min por IP para mitigar spam/DoS sobre un endpoint anónimo. Backend-only, sin
  pantalla de directorio en el frontend todavía.

- **FEAT-004**: Listado de bingos propios del organizador — `GET /api/bingos` (protegido,
  autenticado): un organizador lista los bingos que él mismo creó (nombre del evento, fecha de
  sorteo, cantidad de cartones, costo por cartón), paginado (`page`/`pageSize`, máximo 100 por
  página) y ordenado por fecha de creación descendente. `organizadorId` derivado exclusivamente del
  JWT — nunca de un parámetro de la request, sin fuga entre organizadores. Backend-only, sin
  pantalla "Mis bingos" en el frontend todavía.

- **FEAT-003**: Creación de bingo con generación de cartones — `POST /api/bingos` (protegido,
  autenticado): un organizador crea un bingo (nombre de evento, fecha de sorteo, hasta 5.000
  cartones, costo por cartón) y el sistema genera atómicamente sus cartones (10 números únicos
  entre 1-90 por cartón, GUID único, sin conjuntos repetidos dentro del bingo) usando exclusivamente
  CSPRNG (`RandomNumberGenerator`, RNF-07). Rechaza un segundo bingo mientras el organizador tenga
  uno con sorteo vigente, y limita a 3 creaciones cada 5 minutos por organizador (rate limiting,
  mitigación de abuso de la generación costosa). Backend-only, sin pantalla de creación en el
  frontend todavía.

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
