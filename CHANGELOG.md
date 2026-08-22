# Changelog

Todos los cambios notables de este proyecto se documentan en este archivo.

El formato sigue [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/).

## [Unreleased]

### Added

- **FEAT-009b**: Mail de confirmación de compra con PDF adjunto y reintentos — cada confirmación de
  carrito (FEAT-009a) encola un único mail (agrupando todas las `Compra` que produjo, aunque sean de
  organizadores distintos, vía un `ConfirmacionId` compartido) con el detalle completo de cada compra
  y un PDF adjunto por cartón (10 números + GUID). Primer uso real de MailKit/QuestPDF del proyecto:
  envío desacoplado de la respuesta HTTP (`POST /api/compras/confirmar` nunca espera ni depende del
  mail) vía un patrón outbox — tabla `EnviosMail` en SQL Server + `EnvioMailBackgroundService`
  (`BackgroundService` nativo de .NET, sin librerías nuevas de background jobs) que reintenta hasta 3
  veces con 1 minuto entre intentos antes de marcar el envío `Fallido`. Sobrevive a un reinicio del
  backend (estado persistido, no en memoria). Migración con backfill de `ConfirmacionId` para las
  `Compra` ya existentes en producción (`Guid.NewGuid()` por fila, nunca un valor compartido).
  Conexión SMTP con `StartTls` obligatorio y timeout de 30s; ningún log de este flujo incluye
  PII del comprador, contenido del mail ni credenciales. Nuevo servicio `smtp4dev` en
  `docker-compose.yml` para tests de integración reales sin mocks. Backend-only.

- **FEAT-009a**: Confirmación de compra (núcleo) — `POST /api/compradores/registro` y
  `POST /api/compradores/login` (públicos, primer flujo de auth de comprador, mismo patrón de
  cookie httpOnly `bingocart_auth` ya usado para organizador) y `POST /api/compras/confirmar`
  (autenticado, `[Authorize(Roles = "Comprador")]`): agrupa el carrito por organizador, registra
  una `Compra` por organizador y marca los cartones vendidos vía la tabla `CompraCartones`
  (`CartonId` como PRIMARY KEY, defensa final contra doble venta). `compradorId` se deriva
  exclusivamente del claim JWT, nunca de la request. Coordinación entre Redis (carrito) y SQL
  Server (compra) sin transacción distribuida: revalida las reservas (solo lectura) → confirma la
  compra en SQL (transaccional) → libera el carrito en Redis, solo si el paso anterior tuvo éxito;
  si SQL falla, el TTL de 5 minutos de Redis ya existente es la red de seguridad. Primer uso real
  de roles de Identity (`Organizador`/`Comprador`) del proyecto. Rate limiting nuevo
  (`"compradores"` 5/min/IP, `"compras"` 10/5min por comprador autenticado). Backend-only, sin
  pantalla de checkout en el frontend todavía.

- **FEAT-008b**: Carrito de compras — `POST /api/carrito/cartones/{cartonId}` (agregar),
  `DELETE /api/carrito/cartones/{cartonId}` (quitar, idempotente), `GET /api/carrito` (ver total y
  monto), `POST /api/carrito/tandas/nueva` (descartar y pedir una nueva tanda sin repetir cartones ya
  agregados o descartados en la sesión), todos públicos, identificados por una sesión anónima
  (cookie `bingocart_carrito`, token CSPRNG de 256 bits, sin registro ni login). Primer uso de Redis
  del proyecto: todo el estado del carrito vive ahí (nunca en `Carton`, que sigue inmutable), con
  reserva de 5 minutos por carrito completo que se reinicia en cada agregado y se libera sola por
  TTL — reserva atómica entre sesiones concurrentes resuelta con un script Lua (`EVAL`), sin que dos
  sesiones puedan reservar el mismo cartón. `IDescubrimientoRepository` (FEAT-008a) extendido con
  exclusión de cartones ya agregados/descartados. Rate limiting de 60 requests/5 min por IP;
  `cartonIdsDescartados` limitado a 50 elementos por request (hallazgo de SAST, evita un `NOT IN`
  de SQL desproporcionado). Backend-only, sin pantalla de carrito en el frontend todavía.

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
