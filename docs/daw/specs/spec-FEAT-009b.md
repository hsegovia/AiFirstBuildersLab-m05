# Spec FEAT-009b: Mail de confirmación de compra con PDF adjunto y reintentos

| Field | Value |
|-------|-------|
| Ticket | FEAT-009b |
| PRD | docs/daw/prd/prd-FEAT-009b.md |
| Tier | FEATURE |
| Date | 2026-08-21 |
| Spec loops | 0 |

## Summary

`CompraService.ConfirmarCompraAsync` (FEAT-009a) genera un `ConfirmacionId` compartido para todas
las `Compra` de una misma confirmación de carrito y encola un `EnvioMail` (SQL, outbox) de forma
best-effort, sin bloquear la respuesta HTTP. Un `BackgroundService` de .NET (`PeriodicTimer`, 1
minuto) procesa los envíos pendientes: arma un único mail por confirmación (agrupando todas sus
compras), genera un PDF por cartón vía QuestPDF y lo envía vía MailKit/SMTP. Hasta 3 intentos por
envío, luego se marca `Fallido`. Sin librerías nuevas de background jobs — outbox en SQL Server +
`BackgroundService` ya nativo de .NET.

## Coverage: PRD → blocks

| Requirement | Covered by |
|---|---|
| FR-01 | Block 1, Block 2 |
| FR-02 | Block 2 |
| FR-03 | Block 2, Block 3 |
| FR-04 | Block 2, Block 3 |
| FR-05 | Block 1, Block 2 |
| FR-06 | Block 1, Block 2 |
| FR-07 | Block 2 |
| FR-08 | Block 1, Block 3 |
| NFR-01 | Strategy: `EnvioMail.RegistrarIntentoFallido` fija `ProximoIntentoUtc = ahoraUtc + 1 min` (Block 1); `EnvioMailBackgroundService` usa `PeriodicTimer(TimeSpan.FromMinutes(1))` (Block 3) |
| NFR-02 | Strategy: `EnvioMail.RegistrarIntentoFallido` transiciona a `Fallido` en `Intentos >= 3` (Block 1) |
| NFR-03 | Strategy: `BackgroundService` nativo de .NET + tabla outbox en SQL Server — sin Hangfire ni otra dependencia nueva (Block 3) |

## Dependencies between blocks

Block 1 (Domain) → Block 2 (Application, depende de `EnvioMail`/`Compra.ConfirmacionId`) → Block 3
(Infrastructure, implementa los puertos que Block 2 declara). Orden estrictamente secuencial —
ningún bloque es independiente de su anterior.

## Block 1 — Domain

**Files**
- `backend/BingoCart.Domain/Compras/EnvioMail.cs` (new) — entidad del outbox de mail.
- `backend/BingoCart.Domain/Compras/EstadoEnvioMail.cs` (new) — enum `Pendiente`, `Exitoso`,
  `Fallido`.
- `backend/BingoCart.Domain/Compras/Compra.cs` (modified) — agrega `ConfirmacionId` (Guid) como
  propiedad y como parámetro nuevo de la factory estática `Crear(...)` (Compra no tiene
  constructor público — solo un constructor privado sin parámetros y esta factory; el cambio es en
  la firma de `Crear`, no en un constructor).
- `backend/tests/BingoCart.Domain.Tests/Compras/CompraTests.cs` (modified) — las 2 llamadas
  existentes a `Compra.Crear(...)` (líneas ~20, ~35) pasan a incluir un `Guid.NewGuid()` como
  `confirmacionId`.
- `backend/tests/BingoCart.Infrastructure.Tests/Bingos/BingoRepositoryTests.cs` (modified) — helper
  `SembrarCompraPendiente` (~línea 413) actualizado a la nueva firma.
- `backend/tests/BingoCart.Infrastructure.Tests/Compras/CompraRepositoryTests.cs` (modified) —
  helper `NuevaCompra` (~línea 47) actualizado a la nueva firma.
- `backend/tests/BingoCart.Infrastructure.Tests/Descubrimiento/DescubrimientoRepositoryTests.cs`
  (modified) — helper `SembrarCartonVendidoAsync` (~línea 344) actualizado a la nueva firma.

**Logic**

`EnvioMail` tiene constructor privado sin parámetros y factory estática
`Crear(Guid confirmacionId, Guid compradorId, DateTime ahoraUtc)` que devuelve una instancia en
`Estado = Pendiente`, `Intentos = 0`, `ProximoIntentoUtc = null`. Dos métodos de comportamiento,
ambos lógica pura sin I/O (reciben `ahoraUtc` por parámetro, igual que `Compra.Crear` y el
`TimeProvider` ya inyectado en `CompraService`):

- `RegistrarIntentoFallido(DateTime ahoraUtc)`: incrementa `Intentos`. Si `Intentos >= 3` (NFR-02)
  transiciona `Estado` a `Fallido` y deja de programar reintentos (no toca `ProximoIntentoUtc`).
  Si no, fija `ProximoIntentoUtc = ahoraUtc.AddMinutes(1)` (NFR-01) y permanece `Pendiente`.
- `RegistrarExito()`: fija `Estado = Exitoso`.

`Compra.Crear(...)` agrega `Guid confirmacionId` como nuevo parámetro (entre `compradorId` e
`items`, para mantener los parámetros relacionados con la identidad de la compra agrupados), y
`Compra.ConfirmacionId` queda expuesto como propiedad de solo lectura, igual patrón que el resto de
las propiedades de la entidad (`private init`).

**Data model**

`EnvioMail` (Domain, sin persistencia en este bloque — el mapeo EF Core es Block 3):
- `Id`: Guid, PK.
- `ConfirmacionId`: Guid — agrupa todas las `Compra` de una misma confirmación de carrito.
- `CompradorId`: Guid.
- `Estado`: `EstadoEnvioMail` (Pendiente/Exitoso/Fallido).
- `Intentos`: int, arranca en 0.
- `ProximoIntentoUtc`: DateTime?, null hasta el primer fallo.
- `FechaCreacionUtc`: DateTime.

**Error handling**

No aplica en este bloque — `EnvioMail`/`Compra` no lanzan excepciones nuevas, son entidades puras.

**Required tests**

- [ ] `EnvioMailTests.Crear_DevuelveEstadoInicialPendienteConCeroIntentos` — valida FR-01/FR-08.
- [ ] `EnvioMailTests.RegistrarIntentoFallido_AntesDelTercerIntento_SigueEnPendienteConProximoIntentoEnUnMinuto`
  — valida FR-05/NFR-01.
- [ ] `EnvioMailTests.RegistrarIntentoFallido_EnElTercerIntento_TransicionaAFallido` — valida
  FR-06/NFR-02.
- [ ] `EnvioMailTests.RegistrarExito_FijaEstadoExitoso` — valida FR-03.
- [ ] `CompraTests.Crear_ExponeConfirmacionId` — valida FR-01.

**Completion criterion**

`dotnet test` de `BingoCart.Domain.Tests` en verde, incluyendo los 5 tests nuevos; los 4 archivos
de test de Infrastructure listados arriba compilan contra la nueva firma de `Compra.Crear(...)`
(aunque su suite completa se corre recién al cierre de Block 3, ya que dependen de EF Core/SQL
Server que todavía no tiene el esquema nuevo).

## Block 2 — Application

**Files**
- `backend/BingoCart.Application/Compras/IEnvioMailRepository.cs` (new).
- `backend/BingoCart.Application/Compras/IEmailSender.cs` (new).
- `backend/BingoCart.Application/Compras/ICartonPdfRenderer.cs` (new).
- `backend/BingoCart.Application/Compras/Dtos/DatosParaMailConfirmacion.cs` (new).
- `backend/BingoCart.Application/Compras/Dtos/CompraParaMail.cs` (new).
- `backend/BingoCart.Application/Compras/Dtos/CartonParaMail.cs` (new).
- `backend/BingoCart.Application/Compras/Dtos/EnvioMailMensaje.cs` (new).
- `backend/BingoCart.Application/Compras/IEnvioMailService.cs` (new).
- `backend/BingoCart.Application/Compras/EnvioMailService.cs` (new).
- `backend/BingoCart.Application/Compras/CompraService.cs` (modified).
- `backend/tests/BingoCart.Application.Tests/Compras/CompraServiceTests.cs` (modified) — helper
  `CrearService(...)` (línea ~23) agrega el 6º parámetro, un mock de `IEnvioMailService`.
- `backend/tests/BingoCart.Application.Tests/Compras/EnvioMailServiceTests.cs` (new).

**Logic**

Todos los tipos nuevos viven en `BingoCart.Application/Compras/` — mismo mapeo 1:1 de subcarpeta
por feature ya usado entre Domain/Application/Infrastructure para `Bingos`/`Carritos`/`Compras`.

Puertos (interfaces, sin implementación — Infrastructure los implementa en Block 3, mismo patrón ya
establecido por `ICompraRepository`/`ICarritoRepository`/`IBingoRepository`):

- `IEnvioMailRepository`: `Task EncolarAsync(EnvioMail envio)`;
  `Task<IReadOnlyList<EnvioMail>> ObtenerPendientesAsync(DateTime ahoraUtc)` (filtra
  `Estado == Pendiente AND (ProximoIntentoUtc == null OR ProximoIntentoUtc <= ahoraUtc)`);
  `Task<DatosParaMailConfirmacion?> ObtenerDatosParaEnviarAsync(Guid confirmacionId)` (`null` si no
  hay ninguna `Compra` con ese `ConfirmacionId` — caso defensivo, el dato pudo desaparecer entre
  encolar y procesar); `Task ActualizarAsync(EnvioMail envio)`.
- `IEmailSender`: `Task EnviarAsync(EnvioMailMensaje mensaje)` — recibe EXCLUSIVAMENTE el DTO de
  Application de abajo, nunca un tipo de MailKit (separación de capas, mismo criterio ya aplicado a
  `ICompraRepository` con `DbUpdateException`).
- `ICartonPdfRenderer`: `byte[] Renderizar(Guid cartonId, IReadOnlyList<int> numeros)`.

DTOs, todos `sealed record` (mismo molde que `CompraCreada.cs`/`CartonParaConfirmarCompra.cs`):

- `DatosParaMailConfirmacion(string MailComprador, string NombreComprador, string ApellidoComprador, IReadOnlyList<CompraParaMail> Compras)`.
- `CompraParaMail(Guid CompraId, string NombreOrganizacion, decimal MontoTotal, IReadOnlyList<CartonParaMail> Cartones)`.
- `CartonParaMail(Guid CartonId, IReadOnlyList<int> Numeros)`.
- `EnvioMailMensaje(string Destinatario, string Asunto, string CuerpoHtml, IReadOnlyList<AdjuntoMail> Adjuntos)`
  con `AdjuntoMail(string NombreArchivo, byte[] Contenido)`. Nombrado `EnvioMailMensaje` — NO
  `MailMessage` — para no colisionar con `System.Net.Mail.MailMessage` del BCL y para mantener la
  convención en español ya establecida en esta carpeta.

`EnvioMailService : IEnvioMailService`:

- `EncolarAsync(Guid confirmacionId, Guid compradorId)`: construye `EnvioMail.Crear(confirmacionId, compradorId, ahoraUtc)`
  (vía `TimeProvider` inyectado) y llama `IEnvioMailRepository.EncolarAsync`.
- `ProcesarPendientesAsync()`: obtiene pendientes vía `ObtenerPendientesAsync(ahoraUtc)`. Para CADA
  envío, envuelto en su PROPIO try/catch (una falla no aborta el resto del batch — mitigación R-04
  del threat model):
  1. `ObtenerDatosParaEnviarAsync(envio.ConfirmacionId)` — si `null`, loguea warning con
     ÚNICAMENTE `envio.Id`/`envio.ConfirmacionId` y continúa con el siguiente envío.
  2. Arma UN `EnvioMailMensaje` que detalla TODAS las `CompraParaMail` de esa confirmación (AC-01,
     AC-02) — nunca un mail por compra.
  3. Genera un adjunto PDF por cada cartón de cada compra vía `ICartonPdfRenderer.Renderizar`
     (AC-03).
  4. Llama `IEmailSender.EnviarAsync(mensaje)`.
  5. Éxito → `envio.RegistrarExito()` + `ActualizarAsync(envio)`.
  6. Excepción → `envio.RegistrarIntentoFallido(ahoraUtc)` + `ActualizarAsync(envio)`, logueando
     ÚNICAMENTE `ex.GetType().Name` + `envio.Id`/`envio.ConfirmacionId` — NUNCA `ex.Message`, NUNCA
     PII del comprador ni contenido del mensaje. **Esta es la implementación concreta de R-01/R-02
     (HIGH) del threat model — verificación obligatoria en CODE/SAST.**

`CompraService.ConfirmarCompraAsync` (modificado): inyecta `IEnvioMailService` como 6º parámetro de
constructor. Genera `var confirmacionId = Guid.NewGuid();` una sola vez, antes del `foreach` que
arma `compras` (línea ~75 actual), y lo pasa a CADA llamada a `Compra.Crear(...)` dentro de ese
loop (línea ~83 actual) — así todas las compras de organizadores distintos generadas por una misma
confirmación comparten el mismo `ConfirmacionId`. Después de que `CrearVariasAsync(compras)`
(línea ~100 actual) tiene éxito, agrega:

```csharp
try
{
    await _envioMailService.EncolarAsync(confirmacionId, compradorId);
}
catch (Exception ex)
{
    _logger.LogWarning(
        ex,
        "No se pudo encolar el mail de confirmación para la confirmación {ConfirmacionId}.",
        confirmacionId);
}
```

Este bloque replica EXACTAMENTE el patrón defensivo ya existente para el paso RELEASE (liberar
Redis) unas líneas más abajo en el mismo método (líneas ~106-116 actuales) — catch genérico porque
Application no conoce el tipo concreto de excepción que pueda lanzar la implementación de
Infrastructure, log de warning, nunca relanza. No es un patrón nuevo, es el mismo ya aprobado en
FEAT-009a aplicado a un segundo caso.

**Error handling**

- `EncolarAsync` fallando dentro de `CompraService` nunca hace fallar la confirmación de compra
  (FR-07/AC-06) — capturado y logueado, la compra ya está persistida.
- `ProcesarPendientesAsync` nunca deja que la falla de un envío aborte el procesamiento del resto
  del batch (mitigación R-04).
- `ObtenerDatosParaEnviarAsync` devolviendo `null` se trata como un caso esperable (no una
  excepción) — se saltea ese envío sin marcarlo `Fallido` (los datos podrían reaparecer si fue una
  inconsistencia transitoria; si nunca reaparecen, ese envío queda `Pendiente` indefinidamente,
  aceptado como caso de borde de baja probabilidad ya que requiere que una `Compra` desaparezca
  entre encolar y procesar, algo que ningún flujo del sistema hace hoy).

**Required tests**

- [ ] `EnvioMailServiceTests.EncolarAsync_CreaEnvioEnEstadoPendiente` — valida FR-02.
- [ ] `EnvioMailServiceTests.ProcesarPendientesAsync_ConEnvioExitoso_MarcaExitosoYActualiza` —
  valida FR-03/FR-04.
- [ ] `EnvioMailServiceTests.ProcesarPendientesAsync_ConFallaAntesDelTercerIntento_RegistraIntentoYSigueEnPendiente`
  — valida FR-05.
- [ ] `EnvioMailServiceTests.ProcesarPendientesAsync_ConFallaEnElTercerIntento_MarcaFallido` —
  valida FR-06.
- [ ] `EnvioMailServiceTests.ProcesarPendientesAsync_ConUnEnvioFallandoYOtroExitoso_ProcesaAmbos` —
  valida la mitigación R-04 (una falla no aborta el batch).
- [ ] `EnvioMailServiceTests.ProcesarPendientesAsync_SinDatosParaEnviar_SalteaSinFallar` — sad
  path del caso `null`.
- [ ] `CompraServiceTests.ConfirmarCompraAsync_ConDosOrganizadores_UsaElMismoConfirmacionIdEnAmbasCompras`
  — valida FR-01/AC-01.
- [ ] `CompraServiceTests.ConfirmarCompraAsync_Exitoso_InvocaEncolarAsyncDespuesDeCrearVariasAsync`
  — `MockSequence`, mismo patrón que el test ya existente de orden con `LiberarCarritoConfirmadoAsync`.
  Valida AC-06.
- [ ] `CompraServiceTests.ConfirmarCompraAsync_ConEncolarAsyncLanzandoExcepcion_DevuelveRespuestaIgual`
  — sad path: `EncolarAsync` lanza, la respuesta 200 se devuelve igual, la excepción nunca se
  propaga. Valida FR-07/AC-06.

**Completion criterion**

`dotnet test` de `BingoCart.Application.Tests` en verde, incluyendo los 9 tests nuevos/modificados
listados arriba, con los 3 puertos nuevos mockeados (sin ninguna dependencia real de MailKit,
QuestPDF ni EF Core en este bloque).

## Block 3 — Infrastructure + wiring

**Files**
- `backend/BingoCart.Infrastructure/Data/AppDbContext.cs` (modified) — PRIMERO, antes de la
  migración (la migración no tiene nada que scaffoldear hasta que este archivo declare el nuevo
  `DbSet`).
- `backend/BingoCart.Infrastructure/Data/Migrations/*_AddEnviosMailYConfirmacionId.cs` (new) — vía
  `dotnet ef migrations add`, generada a partir del cambio anterior.
- `backend/BingoCart.Infrastructure/Compras/EnvioMailRepository.cs` (new).
- `backend/BingoCart.Infrastructure/Notificaciones/MailKitEmailSender.cs` (new).
- `backend/BingoCart.Infrastructure/Notificaciones/QuestPdfCartonRenderer.cs` (new).
- `backend/BingoCart.Infrastructure/Notificaciones/EnvioMailBackgroundService.cs` (new).
- `backend/BingoCart.Api/Program.cs` (modified).
- `backend/BingoCart.Infrastructure/BingoCart.Infrastructure.csproj` (modified).
- `backend/BingoCart.Api/appsettings.json` (modified) — claves `Smtp:*` nuevas.
- `backend/BingoCart.Api/appsettings.Development.json` (modified).
- `docker-compose.yml` (modified) — nuevo servicio `smtp4dev`.
- `backend/tests/BingoCart.Infrastructure.Tests/Compras/EnvioMailRepositoryTests.cs` (new).
- `backend/tests/BingoCart.Infrastructure.Tests/Notificaciones/MailKitEmailSenderTests.cs` (new).
- `backend/tests/BingoCart.Infrastructure.Tests/Notificaciones/QuestPdfCartonRendererTests.cs` (new).
- `backend/tests/BingoCart.Infrastructure.Tests/Notificaciones/EnvioMailBackgroundServiceTests.cs`
  (new).

**Logic**

`AppDbContext.cs`: agrega `DbSet<EnvioMail> EnviosMail` y su mapeo en `OnModelCreating` — `HasKey`,
conversión `Estado` (enum) a `int` (mismo patrón ya usado para `MedioPago` en la migración de
FEAT-009a), e índice compuesto sobre `(Estado, ProximoIntentoUtc)` que soporta el filtro de
`ObtenerPendientesAsync`.

Migración EF Core: agrega `ConfirmacionId` (Guid, NOT NULL) a `Compras` — la tabla YA tiene filas
reales en `main` (FEAT-009a), así que la migración hace backfill explícito: cada fila existente
recibe su propio `Guid.NewGuid()` (cada compra pre-existente se trata como su propio lote de
confirmación de un solo elemento), documentado con un comentario en el archivo de migración —
mitigación R-06 del threat model. Crea la tabla `EnviosMail` nueva.

`EnvioMailRepository : IEnvioMailRepository` (EF Core/`AppDbContext`). `ObtenerDatosParaEnviarAsync`
resuelve, para un `confirmacionId`: todas las `Compra` con ese `ConfirmacionId` (join a
`CompraCarton`→`Carton` para los números de cada cartón) más el mail/nombre/apellido del comprador
(vía las tablas de Identity ya existentes, mismo join ya usado en otros puntos del proyecto para
datos de comprador/organizador).

`MailKitEmailSender : IEmailSender`. Arma un `MimeMessage` de MimeKit a partir de
`EnvioMailMensaje`, con el cuerpo HTML construido vía la API `BodyBuilder` de MimeKit (encoding
seguro por defecto, nunca concatenación manual de strings — mitigación R-07). Se conecta con
`SmtpClient` usando `SecureSocketOptions.StartTls` (mitigación R-03, TLS obligatorio) y
`Timeout = 30000` (30s, mitigación R-05). Ante cualquier excepción, loguea ÚNICAMENTE
`ex.GetType().Name` (NUNCA `ex.Message` — mitigación R-01) y relanza, para que `EnvioMailService`
registre el intento fallido. Config: nuevas claves `Smtp:Host`/`Smtp:Port`/`Smtp:User`/
`Smtp:Password`/`Smtp:From` en `appsettings.json`/`appsettings.Development.json`, con el mismo
patrón de secreto-de-desarrollo-sobreescribible-por-variable-de-entorno ya usado en
`docker-compose.yml` para `MSSQL_SA_PASSWORD`/`JWT_SIGNING_KEY`/`Tde:MasterKeyPassword`.

`QuestPdfCartonRenderer : ICartonPdfRenderer`. Genera un PDF de una página por cartón, mostrando
sus 10 números y su GUID (`cartonId`) — el GUID queda impreso porque es la razón de ser de RF-06
(postpuesta), sin la cual el PDF no serviría para esa validación futura.

`EnvioMailBackgroundService : BackgroundService`. Usa `PeriodicTimer(TimeSpan.FromMinutes(1))`
(NFR-01) para invocar `IEnvioMailService.ProcesarPendientesAsync()` en cada tick, envuelto en un
try/catch alrededor de TODA la iteración del loop — segunda capa de la mitigación R-04 (la primera
capa, por-envío, ya está en `EnvioMailService` de Block 2): si `ProcesarPendientesAsync()` lanzara
algo no controlado, el `BackgroundService` no muere permanentemente, solo loguea y sigue esperando
el próximo tick. Vive en `Infrastructure/Notificaciones/` — sin precedente de dónde ubicar un
`IHostedService` en este proyecto (AGENTS.md no define un bucket para "hosting/wiring"), pero
Infrastructure es donde ya viven EF Core/Redis/MailKit/QuestPDF, y este servicio no contiene
lógica de negocio, solo orquesta el polling — coherente con esa capa.

`Program.cs`: registra los 3 adaptadores nuevos + `IEnvioMailService` en el contenedor de DI,
`AddHostedService<EnvioMailBackgroundService>()`, el binding de la config `Smtp:*`, y — como línea
explícita propia, no implícita dentro del wiring de DI — el bootstrap único de QuestPDF:
`QuestPDF.Settings.License = LicenseType.Community;` (confirmado por grep: no existe en ningún
lado del repo; requerido una sola vez al arrancar el proceso desde QuestPDF 2023+ para su licencia
Community).

`BingoCart.Infrastructure.csproj`: agrega `PackageReference` de `MailKit` y `QuestPDF` — ambas ya
declaradas en la tabla Stack de `AGENTS.md`, pre-justificadas a nivel de proyecto, sin necesidad de
justificación puntual para este ticket.

`docker-compose.yml`: agrega el servicio `smtp4dev` con el mismo patrón (imagen, `container_name`,
`ports`, `healthcheck`) ya usado por `db`/`redis` — necesario porque `ComprasControllerTests.cs`
(la suite de integración existente de este mismo flujo) ya corre contra SQL Server/Redis reales vía
`WebApplicationFactory`, sin mocks; cualquier test nuevo que verifique un envío real de punta a
punta necesita el mismo tratamiento. Este contenedor es dev/test-only — nunca debe ser el
`Smtp:Host` de una configuración de Producción (mitigación R-08).

**API contract**

No aplica — este ticket no crea ni modifica ningún endpoint HTTP. El envío de mail es un proceso
interno sin superficie de API.

**Data model**

`EnviosMail` (tabla nueva): `Id` (uniqueidentifier, PK), `ConfirmacionId` (uniqueidentifier, NOT
NULL), `CompradorId` (uniqueidentifier, NOT NULL), `Estado` (int, NOT NULL), `Intentos` (int, NOT
NULL, default 0), `ProximoIntentoUtc` (datetime2, NULL), `FechaCreacionUtc` (datetime2, NOT NULL).
Índice compuesto `(Estado, ProximoIntentoUtc)`. `Compras.ConfirmacionId` (uniqueidentifier, NOT
NULL, agregada por esta migración, con backfill).

**Error handling**

- Falla de conexión/autenticación SMTP → excepción de MailKit, logueada solo por tipo (R-01),
  relanzada, capturada por `EnvioMailService` que registra el intento fallido (Block 2).
- Timeout de SMTP (30s) → misma ruta que la falla de conexión.
- Excepción no controlada dentro del loop del `BackgroundService` → capturada por el try/catch
  externo del propio servicio (R-04, segunda capa), logueada, el servicio sigue vivo para el
  próximo tick.

**Required tests**

- [ ] `EnvioMailRepositoryTests.ObtenerPendientesAsync_RespetaElFiltroDeEstadoYProximoIntento` —
  valida FR-08/AC-07 contra SQL Server real (persistencia sobrevive a un reinicio simulado: se
  reconstruye el repositorio contra el mismo estado ya guardado y el filtro sigue devolviendo lo
  esperado).
- [ ] `EnvioMailRepositoryTests.ObtenerDatosParaEnviarAsync_ArmaCorrectamenteElJoinDeCompradorYCompras`
  — valida FR-03.
- [ ] `EnvioMailRepositoryTests.EncolarAsync_y_ActualizarAsync_Persisten` — valida FR-02/FR-08.
- [ ] `MailKitEmailSenderTests.EnviarAsync_ConSmtpReal_LlegaElMailConAsuntoYAdjuntos` — test de
  integración real contra el `smtp4dev` del compose, sin mocks. Valida AC-01/AC-02/AC-03 de punta a
  punta.
- [ ] `MailKitEmailSenderTests.EnviarAsync_ConFallaDeConexion_NuncaLogueaExMessage` — sad path,
  verificación dirigida de R-01: inspecciona el log capturado y confirma que no contiene el texto
  de la excepción ni ningún dato del mensaje. Cubre tanto la falla de conexión/autenticación como
  el timeout de 30s documentados arriba — ambos son el mismo camino de código (una excepción de
  MailKit propagada desde `SmtpClient`), no dos ramas distintas que requieran tests separados.
- [ ] `QuestPdfCartonRendererTests.Renderizar_DevuelvePdfNoVacioConFirmaValida` — el `byte[]`
  resultante no está vacío y empieza con la firma `%PDF`. Valida AC-03.
- [ ] `EnvioMailBackgroundServiceTests.ExecuteAsync_InvocaProcesarPendientesAsyncEnCadaTick` —
  valida NFR-01 (el timer efectivamente dispara la llamada).
- [ ] `EnvioMailBackgroundServiceTests.ExecuteAsync_ConProcesarPendientesAsyncLanzandoExcepcion_SigueVivoYReintentaEnElProximoTick`
  — valida la segunda capa de la mitigación R-04 (documentada en Error handling arriba): una
  excepción no controlada dentro del loop no mata el `BackgroundService`.

Revisión dirigida (no automatizable como test tradicional, documentada explícitamente per la
sección de riesgos del threat model): confirmar en code review que ningún `_logger.LogX(...)` de
este bloque interpola `ex.Message`, el cuerpo del mail, o cualquier campo de
`DatosParaMailConfirmacion` — solo `envio.Id`/`envio.ConfirmacionId`/`ex.GetType().Name`. Esta
revisión es parte del criterio de cierre de `daw-arch-auditor` en CODE, y de `daw-security-sast`
(F-SAST-10) antes de VERIFY.

**Completion criterion**

`dotnet test` de `BingoCart.Infrastructure.Tests` en verde (incluye los 4 archivos de Block 1
ahora compilando y pasando contra el esquema real), suite completa del proyecto (`Domain`,
`Application`, `Infrastructure`, `Api`) en verde, build sin warnings, y un mail real observable en
`smtp4dev` tras confirmar una compra de punta a punta en un entorno local con el compose levantado.

## Rollback considerations

La migración de este ticket es reversible sin pérdida de datos: `Down()` elimina la tabla
`EnviosMail` (nunca referenciada por otra tabla, sin FK entrante) y la columna `ConfirmacionId` de
`Compras` (el backfill generó valores nuevos que ninguna otra parte del sistema todavía consume —
revertir no rompe ninguna lectura existente, ya que FEAT-009a nunca leyó ese campo). Revertir el
código (Program.cs, los 3 adaptadores nuevos) es un revert de commit estándar, sin pasos manuales.

## Final verification

Confirmar una compra con cartones de 2 organizadores distintos → un único mail (no dos) llega al
`smtp4dev` local, con el detalle de ambas compras y un PDF adjunto por cada cartón, cada uno
mostrando sus 10 números y su GUID. Forzar una falla de SMTP (apagar el `smtp4dev` o apuntar a un
host inválido) → el envío se reintenta 3 veces con ~1 minuto entre cada uno y queda `Fallido`, sin
que la confirmación de compra original haya fallado ni la respuesta HTTP se haya visto afectada.
Reiniciar el proceso backend a mitad de un ciclo de reintentos → el estado persiste en SQL Server y
el ciclo continúa tras el reinicio (AC-07). Ningún log de la aplicación contiene PII del comprador,
el cuerpo del mail, ni una credencial SMTP en ningún punto de este flujo.
