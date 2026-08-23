# Spec FEAT-009c: Confirmación y cancelación manual de pago

| Field | Value |
|-------|-------|
| Ticket | FEAT-009c |
| PRD | docs/daw/prd/prd-FEAT-009c.md |
| Tier | FEATURE |
| Date | 2026-08-22 |
| Spec loops | 0 |

## Summary

`Compra` gana dos transiciones de estado (`ConfirmarPago`/`Cancelar`) operables solo por el
organizador dueño de cada compra propia, vía dos endpoints `PATCH` nuevos más un `GET` de listado
mínimo. Cancelar libera los cartones de esa compra: las 4 queries de "cartón vendido"/"bingo con
compras" del proyecto pasan a excluir compras en `Cancelado` (derivar disponibilidad en query-time,
nunca borrar `CompraCartones`). El mail de cancelación reutiliza el outbox de FEAT-009b — `EnvioMail`
gana un discriminador `TipoEnvio` (Confirmacion/Cancelacion) y una segunda referencia (`CompraId`,
nullable) junto al `ConfirmacionId` ya existente (también vuelto nullable), con su propia rama de
armado de mensaje en `EnvioMailService.ProcesarPendientesAsync` — mismas garantías de reintento (3
intentos, 1 min) que la confirmación, sin outbox nuevo ni `BackgroundService` nuevo.

## Coverage: PRD → blocks

| Requirement | Covered by |
|---|---|
| FR-01 | Block 1, Block 2, Block 3 |
| FR-02 | Block 1, Block 2, Block 3 |
| FR-03 | Block 1, Block 2, Block 3 |
| FR-04 | Block 1, Block 2, Block 3 |
| FR-05 | Block 3 |
| FR-06 | Block 1, Block 2, Block 3 |
| FR-07 | Block 2, Block 3 |
| FR-08 | Block 1, Block 2, Block 3 |
| NFR-01 | Strategy: `CompraOrganizadorService.ListarPropiasAsync` clampea `pageSize` a un máximo de 50 (Block 2) |
| NFR-02 | Strategy: nueva política de rate limiting `"compras-organizador"` (30 req/5min, particionada por claim JWT) en los 2 endpoints `PATCH` (Block 3) |
| NFR-03 | Strategy: `organizadorId` derivado exclusivamente de `ClaimTypes.NameIdentifier` en los 3 endpoints nuevos, nunca de ruta/query/body (Block 3) |

## Dependencies between blocks

Block 1 (Domain) → Block 2 (Application, depende de `Compra.ConfirmarPago/Cancelar`, las 2
excepciones nuevas y la extensión de `EnvioMail`) → Block 3 (Infrastructure + Api, implementa los
puertos que Block 2 declara y expone los endpoints). Orden estrictamente secuencial.

## Block 1 — Domain

**Files**
- `backend/BingoCart.Domain/Compras/Compra.cs` (modified) — `Estado` pasa de `private init` a
  `private set`. Agrega `ConfirmarPago()`/`Cancelar()`.
- `backend/BingoCart.Domain/Compras/Exceptions/CompraEstadoInvalidoException.cs` (new).
- `backend/BingoCart.Domain/Compras/Exceptions/CompraNoEncontradaException.cs` (new).
- `backend/BingoCart.Domain/Compras/EnvioMail.cs` (modified) — agrega `TipoEnvio`, `CompraId`;
  `ConfirmacionId` pasa a nullable; `Crear(...)` se reemplaza por `CrearConfirmacion(...)` +
  `CrearCancelacion(...)`.
- `backend/BingoCart.Domain/Compras/TipoEnvioMail.cs` (new) — enum `Confirmacion`, `Cancelacion`.
- `backend/BingoCart.Application/Compras/EnvioMailService.cs` (modified, solo el call site) —
  `EnvioMail.Crear(...)` → `EnvioMail.CrearConfirmacion(...)`.
- `backend/tests/BingoCart.Domain.Tests/Compras/EnvioMailTests.cs` (modified) — llamadas a `Crear`
  actualizadas a `CrearConfirmacion`.
- `backend/tests/BingoCart.Application.Tests/Compras/EnvioMailServiceTests.cs` (modified) — ídem.
- `backend/tests/BingoCart.Infrastructure.Tests/Compras/EnvioMailRepositoryTests.cs` (modified) —
  ídem.

**Logic**

`Compra.ConfirmarPago()`: si `Estado != PendienteConfirmacionPago` lanza
`CompraEstadoInvalidoException("La compra no está pendiente de confirmación de pago.")` (FR-02/AC-02);
si no, `Estado = Confirmado` (FR-01/AC-01). `Compra.Cancelar()`: mismo guard; si no,
`Estado = Cancelado` (FR-03/AC-03, FR-04/AC-04). Ambos métodos son lógica pura de dominio, sin I/O.

`CompraEstadoInvalidoException`/`CompraNoEncontradaException`: extienden `DomainException`, mismo
shape que `CarritoVacioException`/`BingoNoEncontradoException` (constructor `string message`).
`CompraNoEncontradaException` es el mismo error para "no existe" y "es de otro organizador"
(FR-08/AC-08) — la distinción la hace `CompraOrganizadorService` (Block 2), Domain solo define el
tipo.

`EnvioMail` extendido para RF-06 (reutiliza el outbox de FEAT-009b): `TipoEnvio` (`TipoEnvioMail`,
`private init`), `ConfirmacionId` (`Guid?`, solo seteado para `TipoEnvio = Confirmacion`), `CompraId`
(`Guid?`, `private init`, solo seteado para `TipoEnvio = Cancelacion`). Dos factories reemplazan la
única `Crear(...)` de FEAT-009b: `CrearConfirmacion(Guid confirmacionId, Guid compradorId, DateTime
ahoraUtc)` y `CrearCancelacion(Guid compraId, Guid compradorId, DateTime ahoraUtc)`.
`RegistrarIntentoFallido`/`RegistrarExito` NO cambian — la máquina de estados de reintentos es
genérica a cualquier `TipoEnvio`.

**Data model**

Sin persistencia en este bloque (Domain puro) — el mapeo EF Core de `TipoEnvio`/`CompraId`/
`ConfirmacionId` nullable es Block 3.

**Error handling**

`ConfirmarPago`/`Cancelar` lanzan `CompraEstadoInvalidoException` sobre cualquier transición inválida
— nunca un no-op silencioso (FR-02/FR-04).

**Required tests**

- [ ] `CompraTests.ConfirmarPago_DesdePendiente_TransicionaAConfirmado` — valida FR-01/AC-01.
- [ ] `CompraTests.ConfirmarPago_DesdeConfirmado_LanzaCompraEstadoInvalidoException` — valida
  FR-02/AC-02.
- [ ] `CompraTests.ConfirmarPago_DesdeCancelado_LanzaCompraEstadoInvalidoException` — valida
  FR-02/AC-02.
- [ ] `CompraTests.Cancelar_DesdePendiente_TransicionaACancelado` — valida FR-03/AC-03.
- [ ] `CompraTests.Cancelar_DesdeConfirmado_LanzaCompraEstadoInvalidoException` — valida FR-04/AC-04.
- [ ] `CompraTests.Cancelar_DesdeCancelado_LanzaCompraEstadoInvalidoException` — valida FR-04/AC-04.
- [ ] `EnvioMailTests.CrearConfirmacion_DevuelveTipoEnvioConfirmacionYConfirmacionIdSeteado` — valida
  soporte de RF-06.
- [ ] `EnvioMailTests.CrearCancelacion_DevuelveTipoEnvioCancelacionYCompraIdSeteado` — valida FR-06.

**Completion criterion**

`dotnet test` de `BingoCart.Domain.Tests` en verde con los 8 tests nuevos; `Application.Tests`/
`Infrastructure.Tests` compilan contra la nueva firma de `EnvioMail` (corren completos al cierre de
Block 2/3, mismo patrón secuencial ya usado en FEAT-009b).

## Block 2 — Application

**Files**
- `backend/BingoCart.Application/Compras/ICompraRepository.cs` (modified) — agrega
  `ObtenerPorIdAsync`, `GuardarCambiosAsync`, `ListarPorOrganizadorAsync`.
- `backend/BingoCart.Application/Compras/ComprasPaginadas.cs` (new).
- `backend/BingoCart.Application/Compras/CompraConMonto.cs` (new).
- `backend/BingoCart.Application/Compras/ICompraOrganizadorService.cs` (new).
- `backend/BingoCart.Application/Compras/CompraOrganizadorService.cs` (new).
- `backend/BingoCart.Application/Compras/IEnvioMailService.cs` (modified) — `EncolarAsync` renombrado
  a `EncolarConfirmacionAsync` (simetría con el nuevo `EncolarCancelacionAsync`); agrega
  `EncolarCancelacionAsync`.
- `backend/BingoCart.Application/Compras/EnvioMailService.cs` (modified) — implementa el rename +
  método nuevo; `ProcesarPendientesAsync` ramifica por `TipoEnvio`.
- `backend/BingoCart.Application/Compras/IEnvioMailRepository.cs` (modified) — agrega
  `ObtenerDatosParaCancelacionAsync`.
- `backend/BingoCart.Application/Compras/Dtos/DatosParaMailCancelacion.cs` (new).
- `backend/BingoCart.Application/Compras/Dtos/CompraResumenResponse.cs` (new).
- `backend/BingoCart.Application/Compras/Dtos/CompraListadoResponse.cs` (new).
- `backend/BingoCart.Application/Compras/Dtos/ListarComprasQuery.cs` (new).
- `backend/BingoCart.Application/Compras/CompraService.cs` (modified, solo el call site) —
  `_envioMailService.EncolarAsync(...)` → `EncolarConfirmacionAsync(...)`.
- `backend/tests/BingoCart.Application.Tests/Compras/CompraServiceTests.cs` (modified) — actualiza el
  mock/assert del rename.
- `backend/tests/BingoCart.Application.Tests/Compras/EnvioMailServiceTests.cs` (modified) — `Crear` →
  `CrearConfirmacion` + tests nuevos del tipo Cancelacion.

**Logic**

`ICompraRepository` extendido: `ObtenerPorIdAsync(Guid id)` devuelve la entidad TRACKEADA (sin
`AsNoTracking`, mismo patrón que `IBingoRepository.ObtenerPorIdAsync`). `GuardarCambiosAsync()`
persiste mutaciones sobre una entidad ya trackeada — mismo verbo que `IBingoRepository`, no un
`ActualizarAsync(entidad)` nuevo (nota: `IEnvioMailRepository`, en esta misma carpeta, usa
`ActualizarAsync(envio)` en vez de este patrón — divergencia deliberada, no un descuido: `EnvioMail`
se actualiza siempre completo desde `EnvioMailService`, que nunca lo obtiene vía `ObtenerPorIdAsync`
primero, así que no hay una entidad ya trackeada de la que partir; `Compra` sí sigue el flujo
obtener→mutar→guardar de `IBingoRepository`, por eso mirror ESE patrón y no el de `EnvioMail`).

**`MontoTotal` no es derivable directamente de `Compra`** — `AppDbContext.cs` mapea
`entity.Ignore(c => c.Items)` (el precio real vive únicamente en `CompraCartones.PrecioUnitario`,
fuera del grafo de navegación de `Compra` a propósito, mismo criterio ya documentado en
`CompraCarton.cs`). Por eso `ListarPorOrganizadorAsync(organizadorId, page, pageSize)` NO devuelve
`Compra` desnudo: devuelve `ComprasPaginadas` (record dedicado, mismo patrón que `BingosPaginados`)
con `Items: IReadOnlyList<CompraConMonto>` (`Total: int`), donde `CompraConMonto(Compra Compra,
decimal MontoTotal)` es un record nuevo que empareja cada `Compra` con su monto ya calculado — la
implementación de Infrastructure (Block 3) hace el join/`Sum` contra `CompraCartones` agrupado por
`CompraId` en la misma query, mismo estilo de composición ya usado en
`EnvioMailRepository.ObtenerDatosParaEnviarAsync` (múltiples queries/joins compuestos en la capa de
Infrastructure, nunca un cálculo de negocio en el repositorio — solo agregación SQL). `Compra` sigue
viajando como entidad de Domain (no una fuga de EF Core a la API): el mapeo a
`CompraResumenResponse` sigue ocurriendo en Application (`CompraOrganizadorService`), leyendo
`CompraConMonto.MontoTotal` en vez de intentar derivarlo de `Compra.Items`.

`ICompraOrganizadorService`/`CompraOrganizadorService` (nuevo, separado de `CompraService`: ese es el
flujo del comprador, este es el flujo del organizador, actores y modelo de autorización distintos).
Naming entidad-primero-actor-segundo (`Compra` + `Organizador`), a diferencia del único precedente de
naming por actor en el proyecto (`ICompradorIdentityGateway`, actor-primero) — deliberado: este tipo
vive junto a `CompraService` en `Compras/`, así que agrupar por entidad primero es más descubrible en
esa carpeta específica; `ICompradorIdentityGateway` vive en `Compradores/`, agrupado por actor
porque ahí SÍ es la carpeta del actor. Métodos:

- `ConfirmarPagoAsync(compraId, organizadorId)`: helper privado `ObtenerCompraPropiaAsync(compraId,
  organizadorId)` — mirror exacto de `BingoService.ObtenerBingoPropioSinComprasAsync`: `var compra =
  await _compraRepository.ObtenerPorIdAsync(compraId); if (compra is null || compra.OrganizadorId !=
  organizadorId) { throw new CompraNoEncontradaException("La compra indicada no existe."); } return
  compra;` (FR-08/AC-08). Luego `compra.ConfirmarPago()` (Domain, propaga
  `CompraEstadoInvalidoException` sin capturar — FR-02/AC-02) y `GuardarCambiosAsync()`.
- `CancelarAsync(compraId, organizadorId)`: mismo ownership check, `compra.Cancelar()` (propaga
  `CompraEstadoInvalidoException` — FR-04/AC-04), `GuardarCambiosAsync()`. Después, con la
  cancelación YA persistida: `try { await _envioMailService.EncolarCancelacionAsync(compra.Id,
  compra.CompradorId); } catch (Exception ex) { _logger.LogWarning(ex, "No se pudo encolar el mail
  de cancelación para la compra {CompraId}.", compra.Id); }` — mismo patrón defensivo exacto ya
  usado en `CompraService.ConfirmarCompraAsync` para `EncolarAsync`/
  `LiberarCarritoConfirmadoAsync`: la cancelación nunca falla porque el encolado de mail falló
  (FR-06 best-effort en el paso de encolar, igual que FR-02 en FEAT-009b).
- `ListarPropiasAsync(organizadorId, page, pageSize)`: clampea `pageSize` a un máximo de **50**
  (NFR-01, valor propio e independiente del clamp de 100 que usa `BingoService.ListarPropiosAsync` —
  no es el mismo límite, no se reclama precedente). Llama `ListarPorOrganizadorAsync`, mapea cada
  `CompraConMonto` a `CompraResumenResponse(item.Compra.Id, item.Compra.Estado.ToString(),
  item.MontoTotal, item.Compra.FechaCreacionUtc)` (sin datos del comprador — FR-07/AC-07), devuelve
  `CompraListadoResponse(Items, Total, TotalPaginas, Page, PageSize)` — mismo shape exacto que
  `BingoListadoResponse`, `TotalPaginas` calculado igual (`Math.Ceiling(Total / (double)PageSize)`).

`IEnvioMailService`/`EnvioMailService`: `EncolarAsync` se renombra a `EncolarConfirmacionAsync`
(simetría con el nuevo método, un solo call site a actualizar en `CompraService`). Nuevo
`EncolarCancelacionAsync(compraId, compradorId)` construye `EnvioMail.CrearCancelacion(...)`.
`ProcesarPendientesAsync()` ramifica por `envio.TipoEnvio`: `Confirmacion` → lógica EXISTENTE sin
cambios (`ObtenerDatosParaEnviarAsync(envio.ConfirmacionId!.Value)`, `ArmarMensaje` con PDFs).
`Cancelacion` → `ObtenerDatosParaCancelacionAsync(envio.CompraId!.Value)` (nuevo método de
repositorio); si `null`, loguea warning con solo IDs y saltea (mismo criterio defensivo que el caso
`null` de confirmación); si no, arma un `EnvioMailMensaje` NUEVO vía `ArmarMensajeCancelacion(datos)`
— aviso simple, SIN adjuntos PDF (no hay cartones que confirmar, se están liberando) — y llama
`IEmailSender.EnviarAsync` (sin cambios en esa interfaz). Éxito/fallo (`RegistrarExito`/
`RegistrarIntentoFallido`) ya es genérico a cualquier `TipoEnvio`, sin cambios.

**`ArmarMensajeCancelacion` DEBE aplicar `WebUtility.HtmlEncode` a `NombreComprador` y
`NombreOrganizacion` antes de interpolarlos en el cuerpo HTML** — mismo mitigation que R-07 del
threat model de FEAT-009b (`EnvioMailService.ArmarCuerpoHtml`, mail de confirmación): son campos
suministrados por el comprador/organizador, y el cuerpo del mail es HTML. `MailKitEmailSender`
(Infrastructure, sin cambios en este ticket) asigna `CuerpoHtml` verbatim a `BodyBuilder.HtmlBody` —
un segundo encode ahí corrompería el mail, así que el encoding debe pasar por Application, igual que
en el mail de confirmación.

`DatosParaMailCancelacion(string MailComprador, string NombreComprador, string ApellidoComprador,
Guid CompraId, string NombreOrganizacion)` — deliberadamente mínimo (sin detalle de cartones/monto,
a diferencia de `DatosParaMailConfirmacion`).

**API contract**

No aplica en este bloque — los contratos HTTP se documentan en Block 3.

**Input validation**

`page`/`pageSize` de `ListarComprasQuery`: mismos rangos/atributos que `ListarBingosQuery` (mirror
exacto).

**Error handling**

- `ConfirmarPagoAsync`/`CancelarAsync` propagan `CompraNoEncontradaException`/
  `CompraEstadoInvalidoException` sin capturar — la traducción a HTTP es Block 3.
- `CancelarAsync` nunca deja que una falla al encolar el mail de cancelación aborte la cancelación ya
  persistida (mismo criterio que FEAT-009b).
- `ProcesarPendientesAsync` con un envío tipo `Cancelacion` sin datos (compra desapareció entre
  encolar y procesar) se saltea sin marcar `Fallido`, mismo criterio que el caso `Confirmacion`.

**Required tests**

- [ ] `CompraOrganizadorServiceTests.ConfirmarPagoAsync_ConCompraPropiaPendiente_LlamaConfirmarPagoYGuardaCambios`
  — valida FR-01.
- [ ] `CompraOrganizadorServiceTests.ConfirmarPagoAsync_ConCompraAjena_LanzaCompraNoEncontradaException`
  — valida FR-08.
- [ ] `CompraOrganizadorServiceTests.ConfirmarPagoAsync_ConCompraInexistente_LanzaCompraNoEncontradaException`
  — valida FR-08.
- [ ] `CompraOrganizadorServiceTests.ConfirmarPagoAsync_ConCompraYaConfirmada_PropagaCompraEstadoInvalidoException`
  — valida FR-02.
- [ ] `CompraOrganizadorServiceTests.CancelarAsync_ConCompraPropiaPendiente_TransicionaYEncolaCancelacion`
  — MockSequence (Cancelar/GuardarCambios antes de EncolarCancelacionAsync). Valida FR-03/FR-06.
- [ ] `CompraOrganizadorServiceTests.CancelarAsync_ConEncolarCancelacionLanzandoExcepcion_LaCancelacionIgualQuedaPersistida`
  — sad path del catch defensivo. Valida FR-06 (best-effort).
- [ ] `CompraOrganizadorServiceTests.CancelarAsync_ConCompraAjena_LanzaCompraNoEncontradaException` —
  valida FR-08.
- [ ] `CompraOrganizadorServiceTests.CancelarAsync_ConCompraYaCancelada_PropagaCompraEstadoInvalidoException`
  — valida FR-04.
- [ ] `CompraOrganizadorServiceTests.ListarPropiasAsync_ConPageSizeMayorA50_ClampeaA50` — valida
  NFR-01.
- [ ] `CompraOrganizadorServiceTests.ListarPropiasAsync_DevuelveCompraResumenSinDatosDelComprador` —
  valida FR-07.
- [ ] `EnvioMailServiceTests.EncolarCancelacionAsync_CreaEnvioTipoCancelacionConCompraIdSeteado` —
  valida FR-06.
- [ ] `EnvioMailServiceTests.ProcesarPendientesAsync_ConEnvioTipoCancelacion_ArmaMensajeSinAdjuntosYLoEnvia`
  — valida FR-06.
- [ ] `EnvioMailServiceTests.ProcesarPendientesAsync_ConEnvioTipoCancelacionYDatosNulos_SalteaSinFallar`
  — sad path, mirror del caso `null` de confirmación.

**Completion criterion**

`dotnet test` de `BingoCart.Application.Tests` en verde con los 13 tests nuevos, los 3 puertos
nuevos/modificados mockeados (sin dependencia real de EF Core/MailKit en este bloque).

## Block 3 — Infrastructure + Api

**Files**
- `backend/BingoCart.Infrastructure/Compras/CompraRepository.cs` (modified).
- `backend/BingoCart.Infrastructure/Compras/EnvioMailRepository.cs` (modified).
- `backend/BingoCart.Infrastructure/Data/Migrations/*_AddTipoEnvioYCompraIdAEnviosMail.cs` (new).
- `backend/BingoCart.Infrastructure/Data/AppDbContext.cs` (modified).
- `backend/BingoCart.Infrastructure/Bingos/BingoRepository.cs` (modified) — 2 sitios.
- `backend/BingoCart.Infrastructure/Descubrimiento/DescubrimientoRepository.cs` (modified) — 2
  sitios.
- `backend/BingoCart.Api/Middleware/ExceptionHandlingMiddleware.cs` (modified) — 2 catches nuevos.
- `backend/BingoCart.Api/Controllers/ComprasController.cs` (modified) — 3 endpoints nuevos.
- `backend/BingoCart.Api/Program.cs` (modified) — DI + política de rate limiting nueva.
- Tests nuevos/modificados listados abajo.

**Logic**

`CompraRepository`: `ObtenerPorIdAsync` → `_context.Compras.FirstOrDefaultAsync(c => c.Id == id)`
(trackeado). `GuardarCambiosAsync` → `_context.SaveChangesAsync()`. `ListarPorOrganizadorAsync` →
mirror de la paginación de `BingoRepository.ListarPorOrganizadorAsync` (`Where(OrganizadorId)`,
orden descendente por `FechaCreacionUtc`, `Skip`/`Take`, `CountAsync` para `Total`), PERO el monto de
cada compra no está en `Compra` (`Items` ignorado por EF Core) — se calcula con un `GroupBy(CompraId)`
+ `Sum(PrecioUnitario)` sobre `CompraCartones` para exactamente las `Compra` de la página actual
(nunca sobre toda la tabla), compuesto en memoria con las `Compra` ya paginadas para devolver
`ComprasPaginadas` con `Items: IReadOnlyList<CompraConMonto>` — mismo estilo "múltiples queries
compuestas en Infrastructure" ya usado en `EnvioMailRepository.ObtenerDatosParaEnviarAsync`.

`EnvioMailRepository.ObtenerDatosParaCancelacionAsync(compraId)`: `Compra` por id (`AsNoTracking`),
`null` si no existe; comprador vía `_context.Users` (mismo join que
`ObtenerDatosParaEnviarAsync`); `NombreOrganizacion` vía `_context.Users` sobre `OrganizadorId`.

Migración nueva: `TipoEnvio` (int, NOT NULL, DEFAULT 0 = `Confirmacion` — toda fila existente en
`main` hoy es de tipo confirmación, backfill correcto sin pérdida de datos, documentado en un
comentario). `ConfirmacionId` pasa a nullable. `CompraId` nuevo, nullable. `AppDbContext.cs`: agrega
`entity.Property(e => e.TipoEnvio).HasConversion<int>()` (mismo estilo de documentación que
`Estado`); `ConfirmacionId`/`CompraId` nullable se infieren por convención de EF Core, sin
configuración explícita adicional.

`BingoRepository.cs`, dos sitios modificados (decisión ya confirmada con el usuario):
`TieneComprasRegistradasAsync` y `ObtenerParaCarritoAsync` — ambos excluyen `CompraCartones` cuya
`Compra.Estado == Cancelado` (JOIN a `Compras`, filtro `Estado != Cancelado`).

`DescubrimientoRepository.cs`, dos `FromSqlRaw` modificadas: `AND NOT EXISTS (SELECT 1 FROM
CompraCartones cc INNER JOIN Compras co ON co.Id = cc.CompraId WHERE cc.CartonId = c.Id AND
co.Estado <> {N})`, con `{N}` como placeholder parametrizado de `FromSqlRaw` ligado a
`(int)EstadoCompra.Cancelado` — nunca concatenado, mismo criterio "sin superficie de inyección" ya
documentado en el archivo.

`ExceptionHandlingMiddleware.cs`: `catch (CompraEstadoInvalidoException ex)` → 409 ("EstadoInvalido",
mismo idioma que `BingoConComprasException`); `catch (CompraNoEncontradaException ex)` → 404
("CompraNoEncontrada", mismo idioma que `BingoNoEncontradoException`).

`ComprasController.cs` (se extiende — mismo recurso `api/compras`, ahora con acciones del
organizador): 3 endpoints nuevos, todos `[Authorize(Roles = "Organizador")]`, `organizadorId`
exclusivamente del claim JWT:

- `PATCH api/compras/{id}/confirmar-pago` → `ConfirmarPagoAsync`. 200 con `CompraResumenResponse`
  actualizado, 409, 404, 401/403, 429.
- `PATCH api/compras/{id}/cancelar` → `CancelarAsync`. Misma forma de respuesta.
- `GET api/compras/mias` → `ListarPropiasAsync`, `[FromQuery] ListarComprasQuery`, devuelve
  `CompraListadoResponse`. Sin rate limiting (mirror de `BingosController.Listar`).

Rate limiting: política nueva `"compras-organizador"` (NFR-02: 30 req/5min, particionada por claim
JWT) en los 2 `PATCH` — nunca reutilizar `"compras"` (comprador, 10 req/5min, actor distinto).

`Program.cs`: `AddScoped<ICompraOrganizadorService, CompraOrganizadorService>()` junto a los
registros existentes de `ICompraService`/`ICompraRepository`; política `"compras-organizador"` en
`AddRateLimiter`.

**API contract**

- `PATCH api/compras/{id}/confirmar-pago` — Request: sin body. Response 200:
  `CompraResumenResponse`. Errores: 404 (`CompraNoEncontrada`), 409 (`EstadoInvalido`), 401, 403
  (rol distinto de Organizador), 429. Auth: `[Authorize(Roles = "Organizador")]`.
- `PATCH api/compras/{id}/cancelar` — Request: sin body. Response 200: `CompraResumenResponse`.
  Errores: mismos que confirmar-pago. Auth: igual.
- `GET api/compras/mias?page=&pageSize=` — Request: query `page`/`pageSize` (opcionales, mismos
  defaults que `ListarBingosQuery`). Response 200: `CompraListadoResponse`. Errores: 401, 403.
  Auth: igual. Sin rate limiting.

**Data model**

`EnviosMail` (tabla existente, FEAT-009b): agrega `TipoEnvio` (int, NOT NULL, default 0),
`CompraId` (uniqueidentifier, NULL). `ConfirmacionId` pasa de NOT NULL a NULL. Sin cambios en
`Compras` (la columna `Estado` ya existe desde FEAT-009a).

**Input validation**

`{id}` de ruta: `Guid`, formato ya validado por el binding de ASP.NET Core (400 automático si no
parsea).

**Error handling**

- Falla de conexión SMTP al enviar el mail de cancelación: NO es un camino nuevo — `IEmailSender`
  no distingue el tipo de mensaje, así que ya está cubierto por
  `MailKitEmailSenderTests.EnviarAsync_ConFallaDeConexion_NuncaLogueaExMessage` (FEAT-009b, sin
  cambios en este ticket) más `RegistrarIntentoFallido` (Block 1, Domain, ya genérico a cualquier
  `TipoEnvio`) — no se agrega un test nuevo en este bloque porque no hay comportamiento nuevo que
  probar.
- Compra inexistente/ajena en cualquiera de los 3 endpoints → 404, sin distinguir los casos
  (FR-08/AC-08).
- Transición de estado inválida → 409, nunca aplicada silenciosamente (FR-02/FR-04).

**Required tests**

- [ ] `CompraRepositoryTests.ObtenerPorIdAsync_ConIdExistente_DevuelveLaCompraTrackeada`.
- [ ] `CompraRepositoryTests.ObtenerPorIdAsync_ConIdInexistente_DevuelveNull`.
- [ ] `CompraRepositoryTests.GuardarCambiosAsync_TrasMutarUnaEntidadObtenida_PersisteElCambio` —
  contra SQL Server real.
- [ ] `CompraRepositoryTests.ListarPorOrganizadorAsync_ConTresComprasYPageSizeDos_DevuelveDosYTotalTres`.
- [ ] `EnvioMailRepositoryTests.ObtenerDatosParaCancelacionAsync_ArmaCorrectamenteElDatoDeCompradorYOrganizacion`.
- [ ] `EnvioMailRepositoryTests.ObtenerDatosParaCancelacionAsync_ConCompraInexistente_DevuelveNull`.
- [ ] `BingoRepositoryTests.TieneComprasRegistradasAsync_ConLaUnicaCompraCancelada_DevuelveFalse` —
  valida la decisión de excluir `Cancelado`.
- [ ] `BingoRepositoryTests.ObtenerParaCarritoAsync_ConCartonDeCompraCancelada_LoDevuelveComoDisponible`
  — valida FR-05/AC-05.
- [ ] `DescubrimientoRepositoryTests.ObtenerAleatoriosGlobalAsync_ConCartonDeCompraCancelada_LoIncluyeEntreLosDisponibles`
  — valida FR-05/AC-05.
- [ ] `DescubrimientoRepositoryTests.ObtenerAleatoriosDeBingoAsync_ConCartonDeCompraCancelada_LoIncluyeEntreLosDisponibles`
  — valida FR-05/AC-05.
- [ ] `ComprasControllerTests.ConfirmarPago_ConCompraPropiaPendiente_Devuelve200YEstadoConfirmado` —
  valida AC-01.
- [ ] `ComprasControllerTests.ConfirmarPago_ConCompraAjena_Devuelve404` — valida AC-08.
- [ ] `ComprasControllerTests.ConfirmarPago_ConCompraYaConfirmada_Devuelve409` — valida AC-02.
- [ ] `ComprasControllerTests.Cancelar_ConCompraPropiaPendiente_Devuelve200YLiberaCartones` —
  end-to-end contra la base real: el cartón vuelve a aparecer en descubrimiento. Valida AC-03/AC-05.
- [ ] `ComprasControllerTests.Cancelar_SinRolOrganizador_Devuelve403`.
- [ ] `ComprasControllerTests.ListarMias_ConDosPaginas_RespetaPageSize` — valida AC-07/NFR-01.

Revisión dirigida (mismo criterio que Block 3 de FEAT-009b, threat model + SAST): confirmar que
`EnvioMailService.ArmarMensajeCancelacion` y los 2 catches nuevos de `ExceptionHandlingMiddleware` no
loguean PII del comprador ni contenido del mensaje.

**Completion criterion**

`dotnet test` de la solución completa en verde (incluye Block 1/2 corriendo contra el esquema real);
build sin warnings; verificación manual: confirmar una compra pasa a `Confirmado`; cancelarla pasa a
`Cancelado` y sus cartones vuelven a aparecer en descubrimiento; el mail de cancelación llega vía el
`EnvioMailBackgroundService` ya existente.

## Rollback considerations

Migración reversible sin pérdida de datos real: `Down()` dropea `TipoEnvio`/`CompraId` (columnas
nuevas) y revierte `ConfirmacionId` a NOT NULL (todas las filas existentes ya tienen un valor real —
revertir el código antes que el esquema evita dejar filas `Cancelacion` con `ConfirmacionId` NULL en
una tabla que vuelve a exigir NOT NULL). Revertir el código (Domain/Application/Infrastructure/Api)
es un revert de commit estándar.

## Final verification

Confirmar el pago de una compra propia → pasa a `Confirmado`, un segundo intento de confirmarla
devuelve 409. Cancelar una compra pendiente propia → pasa a `Cancelado`, sus cartones vuelven a
aparecer en `GET /api/cartones/descubrimiento`, y el comprador recibe un mail de cancelación
(observable en `smtp4dev` local) dentro del ciclo de 1 minuto del `EnvioMailBackgroundService`.
Intentar confirmar/cancelar una compra ajena devuelve 404 sin distinguir de "no existe". Un bingo
cuya única compra fue cancelada vuelve a ser editable/eliminable. El listado `GET
/api/compras/mias` nunca expone datos del comprador.
