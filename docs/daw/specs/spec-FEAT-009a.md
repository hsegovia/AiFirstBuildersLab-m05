# Spec FEAT-009a: Confirmar compra (núcleo)

| Field | Value |
|-------|-------|
| Ticket | FEAT-009a |
| PRD | docs/daw/prd/prd-FEAT-009a.md |
| Tier | FEATURE |
| Date | 2026-08-20 |
| Spec loops | 0 |

## Summary

Introduce `Compra` (entidad enteramente nueva, confirmado por impact scan) y la identidad de
comprador (primera cuenta del proyecto que no es organizador, reutilizando la infraestructura de
Identity ya existente). Al confirmar, el carrito de la sesión (Redis, FEAT-008b) se revalida
atómicamente, se agrupa por organizador en compras independientes, se persiste en SQL Server dentro
de una única transacción ("todo o nada"), y solo después de ese commit se libera/vacía el carrito en
Redis. Ningún cartón puede venderse dos veces: la garantía final es una restricción `UNIQUE` en SQL
Server sobre `CartonId`, no solo la reserva de Redis. **Backend-only**, sin pantalla de confirmación
en el frontend todavía.

## Coverage: PRD → blocks

| Requirement | Covered by |
|---|---|
| FR-01 | Block 2, Block 3 |
| FR-02 | Block 2, Block 3 |
| FR-03 | Block 1, Block 2, Block 3 |
| FR-04 | Block 2 |
| FR-05 | Block 1, Block 2 |
| FR-06 | Block 1, Block 2 |
| FR-07 | Block 1 |
| FR-08 | Block 1, Block 2 |
| FR-09 | Block 1, Block 2 |
| NFR-01 | Strategy: unicidad de `CartonId` como `UNIQUE` en SQL Server sobre `CompraCartones` (Block 1) — defensa en profundidad además de la revalidación atómica de Redis (Block 1); test de confirmación concurrente real (Block 2). |
| NFR-02 | Strategy: mismo control de acceso por rol ya usado para organizador (RNF-04) — el comprador nunca accede a datos de otro comprador, verificado por `[Authorize(Roles = "Comprador")]` + `compradorId` derivado exclusivamente del JWT, nunca de un parámetro de request (Block 3). |

## Dependencies between blocks

Block 1 (Domain + Infraestructura) no depende de nada nuevo — extiende Identity (ya en `main`),
`ICarritoRepository` (FEAT-008b) y `IBingoRepository` (FEAT-003/007/008b). Block 2 (Application)
depende de Block 1. Block 3 (Api) depende de Block 2. Orden: 1 → 2 → 3.

**Decisiones cerradas en PLAN (no reabrir en CODE):**

- **El comprador reutiliza `ApplicationUser`/Identity, distinguido por rol** — no una tabla ni un
  sistema de cuentas nuevo. `AspNetRoles`/`AspNetUserRoles` ya existen en el esquema desde
  FEAT-001a pero nunca se usaron (confirmado por impact scan): este ticket es el primer uso real.
  Dos roles: `"Organizador"`, `"Comprador"`, sembrados en el arranque si no existen (`Program.cs`,
  Block 3) — mismo patrón que cualquier seeding idempotente ya usado en el proyecto para TDE
  (`docker-compose`, chequeo "si no existe, crear").
- **`ApplicationUser` (Infrastructure/Identity/ApplicationUser.cs) se modifica**: `NombreOrganizacion`
  y `Telefono` pasan de requeridos a **nullable** (`string?`) — la obligatoriedad para organizador
  se sigue validando en `Organizador.Crear` (Domain) y `OrganizadorService.RegistrarAsync`
  (Application), nunca a nivel de columna; el comportamiento de esos dos flujos no cambia (siempre
  los completan). Se agregan `Apellido`/`Nombre` (`string?`), usados solo por comprador. `Cuit`
  sigue `NOT NULL` y con el mismo índice único — **ambos roles comparten el mismo espacio de CUIT**;
  una persona no puede registrarse como organizador y como comprador con el mismo CUIT bajo cuentas
  distintas (aceptado explícitamente como fuera de alcance, no resuelto en este ticket).
- **`IdentityGateway` implementa DOS interfaces** (`IIdentityGateway` ya existente para organizador,
  `ICompradorIdentityGateway` nueva para comprador) en la misma clase concreta — ambas envuelven el
  mismo `UserManager<ApplicationUser>`/`SignInManager<ApplicationUser>`, evitando duplicar el
  wiring de Identity en dos clases de infraestructura casi idénticas.
- **`JwtTokenService.GenerarToken` cambia de firma**: `GenerarToken(Guid organizadorId, string mail)`
  → `GenerarToken(Guid userId, string mail, string rol)`, agregando `new Claim(ClaimTypes.Role,
  rol)`. Es una modificación a código ya en `main` (FEAT-001b) — el único call site existente
  (`OrganizadorService.AutenticarAsync`) se actualiza para pasar `"Organizador"`, sin cambiar su
  comportamiento observable (el JWT sigue conteniendo `NameIdentifier`/`Email`, ahora además
  `Role`). `[Authorize(Roles = "Comprador")]` funciona sin configuración adicional en
  `Program.cs`: `JwtSecurityTokenHandler` mapea el claim corto `"role"` a `ClaimTypes.Role` por
  default (`DefaultInboundClaimTypeMap`), y `AddJwtBearer` de este proyecto no lo deshabilita.
- **`Comprador` (Domain, nuevo, `Domain/Compradores/`) espeja `Organizador`**: agregado inmutable,
  factory `Crear(apellido, nombre, cuit, mail)` validando CUIT — reutiliza
  `BingoCart.Domain.Organizadores.CuitValidator` directamente (clase estática, sin side effects, ya
  general para cualquier CUIT/CUIL argentino, no acoplada a `Organizador`) en vez de duplicar el
  algoritmo. Excepciones propias en `Domain/Compradores/Exceptions/` (`CuitInvalidoException`,
  `MailYaRegistradoException`, `PasswordInvalidaException`) — mismo patrón de excepciones por
  bounded context ya usado en el proyecto, no se reutilizan las de `Domain/Organizadores/Exceptions/`
  pese a la lógica similar. **`CredencialesInvalidasException` (`Domain/Auth/Exceptions/`) SÍ se
  reutiliza tal cual para el login de comprador** — ya es genérica ("resultado de autenticación, no
  invariante de un agregado", según su propio XML doc), sin necesidad de una versión propia.
- **Los datos del comprador (apellido, nombre, CUIT, mail) se piden UNA VEZ, al registrarse — no se
  vuelven a pedir en cada confirmación de compra.** El flujo es dos pasos: (1)
  `POST /api/compradores/registro` o `/login` (público, fija la cookie `bingocart_auth` con rol
  `Comprador`), (2) `POST /api/compras/confirmar` (autenticado, `Authorize(Roles = "Comprador")`,
  body solo con el medio de pago). FR-02 del PRD ("requerir los datos del comprador al confirmar")
  se satisface con el registro obligatorio previo (FR-01), no con un formulario duplicado — interpretación
  explícita, no ambigüedad a resolver en CODE.
- **`Compra` (Domain, nuevo, `Domain/Compras/`)**: agregado con `Id`, `OrganizadorId`,
  `CompradorId`, `Items` (`IReadOnlyList<ItemCompra>`, record `ItemCompra(Guid CartonId, decimal
  PrecioUnitario)`), `MedioPago` (enum `MedioPago { Efectivo, Transferencia }`), `Estado` (enum
  `EstadoCompra { PendienteConfirmacionPago, Confirmado, Cancelado }` — este ticket solo crea
  compras en `PendienteConfirmacionPago`; las transiciones a `Confirmado`/`Cancelado` son
  FEAT-009c, el enum completo se define acá para no migrar el esquema dos veces), `FechaCreacionUtc`.
  Factory `Crear(...)` valida `Items.Count > 0` como invariante de dominio (defensa en profundidad —
  la validación real de "carrito no vacío" ya ocurre antes, en Application, Block 2).
- **Persistencia de `Compra`/`ItemCompra`**: tabla `Compras` + tabla `CompraCartones`
  (`CompraId`, `CartonId`, `PrecioUnitario`), con **`CartonId` `UNIQUE`** en `CompraCartones` — la
  garantía final de NFR-01/RNF-03: aunque la revalidación de Redis (siguiente punto) tuviera una
  ventana de carrera, SQL Server rechaza una segunda fila con el mismo `CartonId` en cualquier
  compra. `Carton` (Domain) **no se modifica** — sigue inmutable, sin campo de "vendido"; ese estado
  se deriva de la existencia de una fila en `CompraCartones` (mismo criterio de "derivar, no mutar"
  ya usado para "bingo activo" vía `FechaSorteoUtc`).
- **`IBingoRepository.TieneComprasRegistradasAsync(bingoId)` se implementa de verdad por primera
  vez** (`EXISTS` contra `CompraCartones` join `Cartones` por `BingoId`), reemplazando el `false`
  hardcodeado desde FEAT-007. Esto activa `BingoConComprasException` en producción por primera vez
  para `PUT`/`DELETE /api/bingos/{id}` (FEAT-007) — cambio de comportamiento esperado y correcto,
  no un efecto secundario a evitar. El test de integración existente
  `BingoRepositoryTests.TieneComprasRegistradasAsync_ConCualquierBingoId_SiempreDevuelveFalse`
  (impact scan, `BingoRepositoryTests.cs:312-321`) se **reemplaza** en Block 1 por casos reales
  (con/sin compras).
- **Revalidación atómica de Redis: CHECK y COMMIT son dos operaciones separadas, no una.** Ningún
  precedente en el proyecto until ahora combina SQL y Redis en una sola operación lógica (impact
  scan): (1) `ICarritoRepository.RevalidarReservasAsync(sesionId)` — script Lua de **solo lectura**,
  confirma atómicamente que cada `cartonId` del hash `carrito:{sesionId}` sigue teniendo
  `reservado:carton:{cartonId} == sesionId`; devuelve la lista completa si todos son válidos, o la
  lista de los inválidos si no (sin borrar nada en ningún caso). (2) Solo **después** de que la
  transacción SQL de `Compra` commitea exitosamente, `ICarritoRepository.
  LiberarCarritoConfirmadoAsync(sesionId, cartonIds)` — un segundo script Lua que sí borra
  `carrito:{sesionId}` y cada `reservado:carton:{cartonId}`. Si el paso SQL falla, Redis queda
  intacto: nada se pierde, y el TTL de 5 minutos ya existente es la red de seguridad — un fallo real
  deja el carrito reservado un rato más, nunca "confirmado pero con las reservas borradas". Si el
  paso 2 (liberar) fallara después de un commit SQL exitoso, la compra ya quedó persistida
  igual (no se revierte) — se loguea como warning, no como error, y el mismo TTL limpia Redis solo.
- **`IBingoRepository` gana `ObtenerParaConfirmarCompraAsync(IReadOnlyCollection<Guid> cartonIds)`**
  (nuevo método, no se reutiliza ni se modifica `ObtenerParaCarritoAsync` de FEAT-008b — evita un
  breaking change al DTO `CartonParaCarrito`, que es `record` posicional con un solo call site en
  producción hoy pero cuya firma no hay motivo para tocar). Devuelve, para cada `cartonId` cuyo
  `Bingo` todavía existe, `CartonParaConfirmarCompra(CartonId, BingoId, OrganizadorId,
  NombreOrganizacion, NombreEvento)` — el precio de cada ítem **no** se vuelve a leer de SQL: se usa
  el snapshot ya guardado en Redis (`ItemCarrito.PrecioUnitario`, decisión de FEAT-008b), evitando
  que el monto cambie entre agregar al carrito y confirmar.
- **Agrupación por organizador vía `GroupBy(OrganizadorId)`** sobre los resultados combinados de
  Redis (precio) + SQL (`OrganizadorId`/nombres) — una `Compra` por grupo, todas dentro de la misma
  transacción EF Core (`ICompraRepository.CrearVariasAsync`, "todo o nada" real, no solo una
  intención documentada).
- **FR-07 ("ningún cartón vendido vuelve a aparecer en ninguna búsqueda o selección futura") exige
  tocar `DescubrimientoRepository` (FEAT-008a) y `BingoRepository.ObtenerParaCarritoAsync`
  (FEAT-008b) — no alcanza con persistir `CompraCartones` con `UNIQUE`.** Sin este cambio, un
  cartón recién vendido seguiría apareciendo en descubrimiento/nueva tanda apenas su
  `reservado:carton:{cartonId}` de Redis se libera (lo que `LiberarCarritoConfirmadoAsync` hace
  explícitamente al confirmar), y `CarritoService.AgregarAsync` podría dejarlo agregar a un carrito
  nuevo. Ambas queries SQL (`ObtenerAleatoriosGlobalAsync`/`ObtenerAleatoriosDeBingoAsync` en
  `DescubrimientoRepository`, `ObtenerParaCarritoAsync` en `BingoRepository`) suman `AND NOT EXISTS
  (SELECT 1 FROM CompraCartones cc WHERE cc.CartonId = c.Id)` — mismo criterio de "derivar el
  estado, no mutarlo" ya aplicado al resto del ticket. `ObtenerParaCarritoAsync` sigue devolviendo
  `null` sin distinguir "no existe" de "bingo vencido" de "ya vendido" (mismo criterio de
  no-enumeración ya usado en el proyecto).

## Block 1 — Domain + Infraestructura: `Compra`, `Comprador`, revalidación atómica de Redis

**Files**
- `backend/BingoCart.Domain/Compradores/Comprador.cs` (new) — agregado inmutable, factory `Crear`
  validando CUIT vía `CuitValidator` (reutilizado de `Domain.Organizadores`).
- `backend/BingoCart.Domain/Compradores/Exceptions/CuitInvalidoException.cs`,
  `MailYaRegistradoException.cs`, `PasswordInvalidaException.cs` (new) — mismo patrón `sealed class
  : DomainException` que las excepciones existentes de `Organizadores`.
- `backend/BingoCart.Domain/Compras/Compra.cs` (new) — agregado, factory `Crear(organizadorId,
  compradorId, IReadOnlyList<ItemCompra> items, MedioPago medioPago, DateTime ahoraUtc)`, valida
  `items.Count > 0`.
- `backend/BingoCart.Domain/Compras/ItemCompra.cs` (new) — `sealed record ItemCompra(Guid CartonId,
  decimal PrecioUnitario)`.
- `backend/BingoCart.Domain/Compras/MedioPago.cs` (new) — `public enum MedioPago { Efectivo,
  Transferencia }`.
- `backend/BingoCart.Domain/Compras/EstadoCompra.cs` (new) — `public enum EstadoCompra {
  PendienteConfirmacionPago, Confirmado, Cancelado }`.
- `backend/BingoCart.Domain/Compras/Exceptions/CarritoVacioException.cs`,
  `ReservaCarritoInvalidaException.cs` (new) — la segunda transporta
  `IReadOnlyList<Guid> CartonIdsInvalidos` como propiedad pública (no solo en el mensaje), para que
  Block 3 pueda devolverlos estructurados en el body de error.
- `backend/BingoCart.Infrastructure/Identity/ApplicationUser.cs` (modified) — `NombreOrganizacion`/
  `Telefono` a `string?`; agrega `Apellido`/`Nombre` (`string?`).
- `backend/BingoCart.Infrastructure/Data/AppDbContext.cs` (modified) — `DbSet<Compra>` (con
  `OwnsMany` o entidad separada `CompraCarton` mapeada a la tabla `CompraCartones`,
  `HasIndex(cc => cc.CartonId).IsUnique()`); ajusta el mapeo de `ApplicationUser` para las columnas
  ahora nullable + las dos nuevas.
- `backend/BingoCart.Infrastructure/Data/Migrations/*` (new) — migración con las tablas
  `Compras`/`CompraCartones`, las columnas nuevas/nullable de `AspNetUsers`, y el seeding de los
  roles `Organizador`/`Comprador` (vía `RoleManager<IdentityRole<Guid>>` en `Program.cs`, no en la
  migración — la migración solo crea el esquema).
- `backend/BingoCart.Application/Compradores/ICompradorIdentityGateway.cs` (new) — puerto: mismo
  shape que `IIdentityGateway` (`ExisteMailAsync`, `CrearUsuarioAsync(Comprador, string password)`,
  `AutenticarAsync(string mail, string password)`).
- `backend/BingoCart.Infrastructure/Identity/IdentityGateway.cs` (modified) — implementa también
  `ICompradorIdentityGateway`; `CrearUsuarioAsync(Comprador, password)` asigna el rol `"Comprador"`
  vía `UserManager.AddToRoleAsync` tras crear el `ApplicationUser`.
- `backend/BingoCart.Infrastructure/Auth/JwtTokenService.cs` (modified) — firma nueva con `rol`
  (ver "Decisiones cerradas en PLAN").
- `backend/BingoCart.Application/Carritos/ICarritoRepository.cs` (modified) — agrega
  `RevalidarReservasAsync(string sesionId)` y `LiberarCarritoConfirmadoAsync(string sesionId,
  IReadOnlyCollection<Guid> cartonIds)`.
- `backend/BingoCart.Application/Carritos/Dtos/RevalidacionCarrito.cs` (new) — `sealed record
  RevalidacionCarrito(bool EsValido, IReadOnlyList<ItemCarrito> Items, IReadOnlyList<Guid>
  CartonIdsInvalidos)`.
- `backend/BingoCart.Infrastructure/Carritos/CarritoRepository.cs` (modified) — implementa los dos
  métodos nuevos con scripts Lua propios (`ScriptRevalidar`, `ScriptLiberar`), mismo patrón `ARGV`
  del script existente (`sesionId`/`cartonId` nunca concatenados al cuerpo del script).
- `backend/BingoCart.Application/Bingos/IBingoRepository.cs` (modified) — agrega
  `ObtenerParaConfirmarCompraAsync(IReadOnlyCollection<Guid> cartonIds)`; el XML doc de
  `TieneComprasRegistradasAsync` se actualiza (ya no dice "hoy siempre false").
- `backend/BingoCart.Application/Compras/Dtos/CartonParaConfirmarCompra.cs` (new) — `sealed record
  CartonParaConfirmarCompra(Guid CartonId, Guid BingoId, Guid OrganizadorId, string
  NombreOrganizacion, string NombreEvento)`.
- `backend/BingoCart.Infrastructure/Bingos/BingoRepository.cs` (modified) — implementa
  `ObtenerParaConfirmarCompraAsync` (LINQ, JOIN `Cartones`+`Bingos`+`Users`, `WHERE c.Id IN
  (cartonIds)`, sin filtro de "activo" — a diferencia de `ObtenerParaCarritoAsync`, acá el cartón ya
  pasó la revalidación de reserva; si su bingo venció en el ínterin es un caso de borde aceptado,
  fuera de alcance de este ticket) y `TieneComprasRegistradasAsync` real. **Modifica también
  `ObtenerParaCarritoAsync`** (FEAT-008b): suma `!_context.CompraCartones.Any(cc => cc.CartonId ==
  c.Id)` al `Where` existente — un cartón ya vendido nunca se puede volver a agregar a un carrito
  (FR-07, ver "Decisiones cerradas en PLAN").
- `backend/BingoCart.Infrastructure/Descubrimiento/DescubrimientoRepository.cs` (modified,
  FEAT-008a) — ambas queries `FromSqlRaw` (`ObtenerAleatoriosGlobalAsync`/
  `ObtenerAleatoriosDeBingoAsync`) suman `AND NOT EXISTS (SELECT 1 FROM CompraCartones cc WHERE
  cc.CartonId = c.Id)`, junto a la cláusula `NOT IN` de exclusión ya existente (FEAT-008b) — mismo
  mecanismo de concatenación de texto ya verificado como seguro (sin parámetros de usuario
  involucrados, la subquery es texto fijo).
- `backend/BingoCart.Application/Compras/ICompraRepository.cs` (new) — puerto:
  `Task CrearVariasAsync(IReadOnlyList<Compra> compras)` (una transacción EF Core para todas).
- `backend/BingoCart.Infrastructure/Compras/CompraRepository.cs` (new) — implementa
  `CrearVariasAsync` con `_context.Database.BeginTransactionAsync()` + `AddRange` + `SaveChangesAsync`
  + commit; deja que una violación de `UNIQUE` en `CompraCartones.CartonId` se propague como
  `DbUpdateException` sin capturar (Application, Block 2, la traduce).

**Logic**
`Compra`/`Comprador` puros, sin I/O. `CarritoRepository`/`BingoRepository`/`CompraRepository`
infraestructura pura — no deciden negocio (qué compras se generan por organizador lo decide
Application, Block 2).

**API contract**
N/A — este bloque no expone ningún endpoint.

**Data model**
- `Compras`: `Id` (PK), `OrganizadorId`, `CompradorId`, `MedioPago`, `Estado`, `FechaCreacionUtc`.
- `CompraCartones`: `CompraId` (FK), `CartonId` (**UNIQUE**), `PrecioUnitario`.
- `AspNetUsers`: `NombreOrganizacion`/`Telefono` nullable; `Apellido`/`Nombre` nuevas, nullable.
- `AspNetRoles`: 2 filas sembradas (`Organizador`, `Comprador`), primer uso real de esta tabla.

**Input validation**
`sesionId`/`cartonId`/`compradorId` llegan validados por el llamador (Application, Block 2) — este
bloque no revalida formato, solo aplica las invariantes de dominio ya descriptas.

**Error handling**
Sin excepciones nuevas propias de infraestructura — `Compra.Crear`/`Comprador.Crear` lanzan las
excepciones de dominio ya listadas; una violación de `UNIQUE` en `CompraCartones` se propaga sin
capturar (Application decide qué hacer).

**Required tests**
- [ ] `CompraTests` (Domain): `Compra.Crear` con `items` vacío → `ArgumentException` (invariante
  interna, no una excepción de dominio pública — no hay AC que lo pida explícitamente, es defensa
  en profundidad); con al menos 1 ítem → construye correctamente.
- [ ] `CompradorTests` (Domain): `Comprador.Crear` con CUIT inválido → `CuitInvalidoException`; con
  CUIT válido → construye correctamente.
- [ ] `CarritoRepositoryTests` (Infrastructure, extendido, Redis real):
  `RevalidarReservasAsync` con todas las reservas vigentes → `EsValido == true`, `Items` con los
  precios correctos — valida AC-08 (parte de infraestructura).
- [ ] `CarritoRepositoryTests`: `RevalidarReservasAsync` con una reserva ya expirada (TTL corto +
  `Task.Delay`, mismo patrón que FEAT-008b) → `EsValido == false`, `CartonIdsInvalidos` contiene
  exactamente ese `cartonId` — valida AC-07.
- [ ] `CarritoRepositoryTests`: `RevalidarReservasAsync` NUNCA borra ninguna clave (verificado con
  `KeyExistsAsync` antes/después) — valida la decisión de PLAN "CHECK de solo lectura".
- [ ] `CarritoRepositoryTests`: `LiberarCarritoConfirmadoAsync` borra `carrito:{sesionId}` y todos
  los `reservado:carton:{cartonId}` pasados — valida AC-09 (parte de infraestructura).
- [ ] `CarritoRepositoryTests` (concurrencia real, `Task.WhenAll`): dos sesiones con carritos que
  comparten un `cartonId` llaman `RevalidarReservasAsync` simultáneamente → cada una ve como
  inválido únicamente el ítem que no le pertenece a ella (el chequeo es de lectura, no hay "ganador"
  acá — el ganador real se decide en `IntentarAgregarAsync`, ya cubierto en FEAT-008b; este test
  confirma que la lectura concurrente es consistente, no que arbitra).
- [ ] `BingoRepositoryTests` (Infrastructure, integración real, **reemplaza** el test de "siempre
  false"): `TieneComprasRegistradasAsync` con un bingo sin compras → `false`; con un bingo que tiene
  al menos una fila en `CompraCartones` → `true`.
- [ ] `BingoRepositoryTests`: `ObtenerParaConfirmarCompraAsync` con 2 `cartonId` reales de
  organizadores distintos → devuelve `OrganizadorId`/nombres correctos para ambos, sin mezclarlos.
- [ ] `BingoRepositoryTests`: `ObtenerParaCarritoAsync` con un cartón que ya tiene una fila en
  `CompraCartones` (compra sembrada directamente) → `null`, aunque su bingo siga activo — valida
  AC-06 (parte de infraestructura, un cartón vendido no puede volver a agregarse a un carrito).
- [ ] `DescubrimientoRepositoryTests` (Infrastructure, extendido, FEAT-008a): `
  ObtenerAleatoriosGlobalAsync` con un bingo activo que tiene 5 cartones, 1 de ellos ya vendido
  (fila sembrada en `CompraCartones`) → nunca devuelve ese cartón entre los resultados, aunque no
  esté en la lista de `excluirCartonIds` — valida AC-06 (descubrimiento global).
- [ ] `DescubrimientoRepositoryTests`: mismo caso para `ObtenerAleatoriosDeBingoAsync` (Método 2) —
  valida AC-06 (descubrimiento por organizador).
- [ ] `CompraRepositoryTests` (Infrastructure, integración real): `CrearVariasAsync` con 2 `Compra`
  de organizadores distintos, una con `MedioPago.Transferencia` y otra con `MedioPago.Efectivo` →
  ambas persistidas con sus `CompraCartones`, cada una con su `MedioPago` correcto y `Estado ==
  PendienteConfirmacionPago` — valida AC-04 y AC-05 (parte de infraestructura, ambos medios de pago
  llegan al mismo estado).
- [ ] `CompraRepositoryTests`: `CrearVariasAsync` con un `cartonId` que ya existe en
  `CompraCartones` (de una compra previa sembrada) → `DbUpdateException` (violación de `UNIQUE`),
  ninguna de las compras del intento actual queda persistida (la transacción revierte todo) — valida
  NFR-01 a nivel de infraestructura.
- [ ] `JwtTokenServiceTests` (existente, extendido): `GenerarToken` con `rol = "Comprador"` → el JWT
  decodificado contiene el claim `role` con ese valor; regresión: con `rol = "Organizador"` el
  comportamiento es idéntico al de antes de este ticket.

**Completion criterion**
Los 16 tests pasan contra SQL Server + Redis reales; ningún cartón puede insertarse dos veces en
`CompraCartones`; `RevalidarReservasAsync` nunca modifica el estado de Redis; `Carton` (Domain) sigue
sin ningún campo nuevo; ningún cartón con una fila en `CompraCartones` puede volver a agregarse a un
carrito ni aparecer en descubrimiento (global o por organizador).

## Block 2 — Application: `CompradorService`, `CompraService`

**Files**
- `backend/BingoCart.Application/Compradores/Dtos/RegistrarCompradorRequest.cs`,
  `RegistrarCompradorResponse.cs`, `LoginCompradorRequest.cs`, `LoginCompradorResponse.cs` (new) —
  mismo shape que los equivalentes de `Organizadores` (`RegistrarOrganizadorRequest` tiene
  `NombreOrganizacion`/`Cuit`/`Mail`/`Telefono`/`Password`; el de comprador tiene
  `Apellido`/`Nombre`/`Cuit`/`Mail`/`Password`, sin teléfono — no lo pide el PRD).
- `backend/BingoCart.Application/Compradores/ICompradorService.cs`,
  `CompradorService.cs` (new) — `RegistrarAsync`/`AutenticarAsync`, calco exacto de
  `OrganizadorService.RegistrarAsync`/`AutenticarAsync` (mismo orden: Domain valida → unicidad de
  mail → gateway → JWT con `rol: "Comprador"`).
- `backend/BingoCart.Application/Organizadores/OrganizadorService.cs` (modified) — el único cambio
  es la llamada a `_jwtTokenService.GenerarToken(organizadorId, request.Mail, "Organizador")`.
- `backend/BingoCart.Application/Compras/Dtos/ConfirmarCompraRequest.cs` (new) — `sealed record
  ConfirmarCompraRequest(MedioPago MedioPago)`.
- `backend/BingoCart.Application/Compras/Dtos/CompraCreada.cs` (new) — `sealed record
  CompraCreada(Guid CompraId, Guid OrganizadorId, string NombreOrganizacion, int CantidadCartones,
  decimal MontoTotal)`.
- `backend/BingoCart.Application/Compras/Dtos/ConfirmarCompraResponse.cs` (new) — `sealed record
  ConfirmarCompraResponse(IReadOnlyList<CompraCreada> Compras)`.
- `backend/BingoCart.Application/Compras/ICompraService.cs`,
  `CompraService.cs` (new) — `ConfirmarCompraAsync(string sesionId, Guid compradorId, MedioPago
  medioPago)`, con `TimeProvider` inyectado:
  1. `itemsCarrito = await _carritoRepository.ObtenerItemsAsync(sesionId)`; si vacío →
     `throw new CarritoVacioException(...)` (AC-02), sin llamar a nada más.
  2. `revalidacion = await _carritoRepository.RevalidarReservasAsync(sesionId)`; si
     `!revalidacion.EsValido` → `throw new ReservaCarritoInvalidaException(revalidacion.
     CartonIdsInvalidos, ...)` (AC-07).
  3. `datosCartones = await _bingoRepository.ObtenerParaConfirmarCompraAsync(revalidacion.Items.
     Select(i => i.CartonId).ToList())` — si algún `cartonId` de `revalidacion.Items` no tiene
     correspondencia (bingo eliminado entre el agregado y la confirmación, caso de borde), se trata
     igual que una reserva inválida: se agrega a la lista de `CartonIdsInvalidos` y se lanza
     `ReservaCarritoInvalidaException`.
  4. Combina `revalidacion.Items` (precio) con `datosCartones` (organizador) por `CartonId`, agrupa
     por `OrganizadorId` (`GroupBy`), arma una `Compra.Crear(...)` por grupo con `ahoraUtc` de
     `_timeProvider`.
  5. `await _compraRepository.CrearVariasAsync(compras)` — si lanza `DbUpdateException` (violación
     `UNIQUE`, carrera perdida contra otra confirmación), se traduce a `ReservaCarritoInvalidaException`
     sin `CartonIdsInvalidos` detallados (no hay forma barata de saber cuál violó sin una consulta
     extra — se documenta como limitación aceptada, caso extremadamente raro dado que Redis ya
     serializa la reserva).
  6. `await _carritoRepository.LiberarCarritoConfirmadoAsync(sesionId, cartonIds)` — si falla, se
     loguea como *warning* (no se relanza; la compra ya está persistida, ver decisión de PLAN).
  7. Devuelve `ConfirmarCompraResponse` con un `CompraCreada` por grupo.

**Logic**
`CompraService` es el único punto que combina Redis (`ICarritoRepository`), SQL de bingos
(`IBingoRepository`) y SQL de compras (`ICompraRepository`) — ninguno de los tres se conoce entre sí.

**API contract**
N/A — este bloque no expone ningún endpoint (eso es Block 3).

**Data model**
Sin cambios adicionales a los ya declarados en Block 1.

**Input validation**
`sesionId`/`compradorId` se asumen no vacíos/ya autenticados (el Controller, Block 3, lo garantiza).
`MedioPago` es un enum — un valor fuera del rango rechaza automáticamente en el model binding (Block
3), este servicio no revalida.

**Error handling**
`CarritoVacioException` (carrito vacío), `ReservaCarritoInvalidaException` (reserva inválida o
perdida contra otra confirmación) — ambas nuevas en Block 1, lanzadas acá. `MailYaRegistradoException`/
`PasswordInvalidaException`/`CuitInvalidoException` (Compradores) y `CredencialesInvalidasException`
(reutilizada) en `CompradorService`.

**Required tests**
- [ ] `CompradorServiceTests` (unit, mock de `ICompradorIdentityGateway`): `RegistrarAsync` con CUIT
  válido y mail no existente → éxito, invoca `CrearUsuarioAsync` — valida el registro de comprador.
- [ ] `CompradorServiceTests`: `RegistrarAsync` con CUIT inválido → `CuitInvalidoException`, sin
  invocar al gateway (mismo patrón que `OrganizadorServiceTests.
  RegistrarAsync_ConCuitInvalido_LanzaExcepcionYNoLlamaAlGateway`).
- [ ] `CompradorServiceTests`: `RegistrarAsync` con mail ya existente → `MailYaRegistradoException`,
  sin invocar `CrearUsuarioAsync`.
- [ ] `CompradorServiceTests`: `RegistrarAsync` con password que no cumple la política de Identity
  → `PasswordInvalidaException` (mismo patrón que
  `OrganizadorServiceTests.RegistrarAsync_ConPasswordInvalida_LanzaPasswordInvalidaException`).
- [ ] `CompradorServiceTests`: `AutenticarAsync` con credenciales válidas → JWT con `rol:
  "Comprador"`; con credenciales inválidas → `CredencialesInvalidasException`.
- [ ] `OrganizadorServiceTests` (existente, regresión): `AutenticarAsync` sigue devolviendo un JWT
  válido — confirma que el cambio de firma de `GenerarToken` no rompió el flujo de organizador.
- [ ] `CompraServiceTests` (unit, mocks de los 3 repositorios): `ConfirmarCompraAsync` con carrito
  vacío → `CarritoVacioException`, sin invocar `RevalidarReservasAsync` — valida AC-02
  (orquestación).
- [ ] `CompraServiceTests`: `ConfirmarCompraAsync` con `RevalidarReservasAsync` devolviendo
  `EsValido: false` → `ReservaCarritoInvalidaException` con los `CartonIdsInvalidos` correctos, sin
  invocar `CrearVariasAsync` — valida AC-07 (orquestación).
- [ ] `CompraServiceTests`: `ConfirmarCompraAsync` con 3 cartones de 2 organizadores distintos
  (revalidación válida) → invoca `CrearVariasAsync` con exactamente 2 `Compra`, cada una con sus
  cartones correctos — valida AC-03 (orquestación).
- [ ] `CompraServiceTests`: `ConfirmarCompraAsync` exitoso → invoca `LiberarCarritoConfirmadoAsync`
  DESPUÉS de `CrearVariasAsync` (orden verificado con `Mock.Verify`/`MockSequence`) — valida AC-09 y
  el orden CHECK→SQL→COMMIT de la decisión de PLAN.
- [ ] `CompraServiceTests`: `ConfirmarCompraAsync` con `CrearVariasAsync` lanzando `DbUpdateException`
  → `ReservaCarritoInvalidaException`, `LiberarCarritoConfirmadoAsync` NUNCA invocado (el carrito no
  se vacía si la compra no se persistió) — valida la decisión de PLAN de no liberar Redis si SQL
  falla.

**Completion criterion**
Los 11 tests pasan; `OrganizadorService`/`BingoService` (FEAT-001b/007) siguen pasando sus propias
suites sin cambios de comportamiento (regresión cero); ninguna llamada a
`LiberarCarritoConfirmadoAsync` ocurre sin un `CrearVariasAsync` exitoso previo.

## Block 3 — Api: `CompradoresController`, `ComprasController`, wiring

**Files**
- `backend/BingoCart.Api/Controllers/CompradoresController.cs` (new) — `[AllowAnonymous]`,
  `[Route("api/compradores")]`, `POST /registro` y `POST /login`, calco exacto de
  `OrganizadoresController` (misma cookie `bingocart_auth`, mismos flags `HttpOnly`/`Secure`/
  `SameSite=Strict`, mismo criterio de "fijar la cookie es transporte, no negocio").
  `[EnableRateLimiting("compradores")]` en ambas acciones.
- `backend/BingoCart.Api/Controllers/ComprasController.cs` (new) — `[ApiController]`,
  `[Route("api/compras")]`, un único endpoint:
  ```csharp
  [HttpPost("confirmar")]
  [Authorize(Roles = "Comprador")]
  [EnableRateLimiting("compras")]
  [ProducesResponseType(typeof(ConfirmarCompraResponse), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  public async Task<ActionResult<ConfirmarCompraResponse>> Confirmar([FromBody] ConfirmarCompraRequest request)
  {
      var sesionId = ObtenerSesionIdDeCookie(); // lee bingocart_carrito, SIN crear una nueva si falta
      var compradorId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
      var respuesta = await _compraService.ConfirmarCompraAsync(sesionId, compradorId, request.MedioPago);
      return Ok(respuesta);
  }
  ```
  A diferencia de `CarritoController.ObtenerOCrearSesionId` (que SÍ crea una cookie nueva si falta),
  acá si no hay cookie `bingocart_carrito` el carrito está vacío por definición → se trata igual que
  un carrito vacío real (`CarritoVacioException`, sin crear ninguna cookie en un endpoint autenticado
  de escritura).
- `backend/BingoCart.Api/Middleware/ExceptionHandlingMiddleware.cs` (modified) — agrega 5 catch:
  `CarritoVacioException` → 400, `ReservaCarritoInvalidaException` → 409 (incluye
  `CartonIdsInvalidos` en el body de error), `CuitInvalidoException`/`MailYaRegistradoException`/
  `PasswordInvalidaException` (Compradores) → 400/409/400 respectivamente, mismo patrón que sus
  equivalentes de `Organizadores`.
- `backend/BingoCart.Api/Program.cs` (modified):
  - `AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new
    JsonStringEnumConverter()))` — primer uso de un enum en un contrato JSON del proyecto
    (`MedioPago`); sin esto, `"medioPago": "Efectivo"` no deserializa (el default de
    `System.Text.Json` espera el valor numérico subyacente).
  - Seeding de roles `Organizador`/`Comprador` vía `RoleManager<IdentityRole<Guid>>` al arrancar,
    idempotente (`if (!await roleManager.RoleExistsAsync(...))`).
  - `AddScoped<ICompradorIdentityGateway>`/`AddScoped<ICompradorService, CompradorService>`/
    `AddScoped<ICompraService, CompraService>`/`AddScoped<ICompraRepository, CompraRepository>`
    (el `IdentityGateway` concreto ya registrado para `IIdentityGateway` se registra también para
    `ICompradorIdentityGateway`, misma instancia por scope).
  - Dos políticas de rate limiting nuevas: `"compradores"` (5 req/min/IP, mismo límite que
    `"registro"` — cubre tanto registro como login de comprador, mismo criterio que
    `OrganizadoresController` NO tiene un rate limit propio en `Login` hoy, pero acá sí se agrega
    porque el comprador es un endpoint nuevo sin ese precedente de excepción) y `"compras"` (10
    req/5min, particionado por el claim `NameIdentifier` del JWT — mismo mecanismo que `"bingos"`,
    no por IP, porque el endpoint ya está autenticado).

**API contract**
- `POST /api/compradores/registro` — body `{apellido, nombre, cuit, mail, password}`. Response 201:
  `{id, apellido, nombre, mail}`. Response 400: CUIT inválido / password inválida. Response 409:
  mail ya registrado.
- `POST /api/compradores/login` — body `{mail, password}`. Response 200: fija cookie
  `bingocart_auth` (rol `Comprador`), body `{}`. Response 401: credenciales inválidas.
- `POST /api/compras/confirmar` — autenticado (rol `Comprador`), body `{medioPago: "Efectivo" |
  "Transferencia"}`. Response 200: `{compras: [{compraId, organizadorId, nombreOrganizacion,
  cantidadCartones, montoTotal}, ...]}`. Response 400: carrito vacío. Response 409: reserva
  inválida/perdida (`{error: "ReservaCarritoInvalida", cartonIdsInvalidos: [...]}`).

**Input validation**
`MedioPago` fuera del enum rechaza automáticamente (400, model binding). `apellido`/`nombre`
validados como no vacíos vía DataAnnotations en el DTO (mismo patrón que `NombreOrganizacion` en
`RegistrarOrganizadorRequest`).

**Error handling**
Los 5 catches nuevos listados arriba. Sin otros errores de dominio nuevos en este bloque.

**Required tests**
- [ ] `CompradoresControllerTests` (integración, `WebApplicationFactory` + SQL Server real):
  `POST /api/compradores/registro` con datos válidos → 201, la cuenta queda en `AspNetUsers` con
  rol `Comprador` (verificado consultando `AspNetUserRoles`).
- [ ] `CompradoresControllerTests`: `POST /api/compradores/registro` con CUIT inválido → 400
  `CuitInvalido`; con mail ya registrado → 409 `MailYaRegistrado`; con password que no cumple la
  política de Identity → 400 `PasswordInvalida` — valida los 3 catches nuevos de este bloque a
  nivel HTTP (F-SPEC-16).
- [ ] `CompradoresControllerTests`: `POST /api/compradores/login` con credenciales válidas → 200,
  fija la cookie `bingocart_auth`; el JWT decodificado del valor de la cookie contiene `role:
  Comprador`.
- [ ] `ComprasControllerTests` (integración, `WebApplicationFactory` + SQL Server + Redis reales):
  flujo completo — sembrar un bingo activo con cartones reales, agregar 2 al carrito vía
  `POST /api/carrito/cartones/{id}` (FEAT-008b), registrar/loguear un comprador, `POST
  /api/compras/confirmar` con `medioPago: Transferencia` → 200 con 1 `CompraCreada`,
  `cantidadCartones: 2`; `GET /api/carrito` posterior → carrito vacío (AC-09 end-to-end).
- [ ] `ComprasControllerTests`: mismo flujo pero agregando cartones de 2 organizadores distintos →
  200 con 2 `CompraCreada` — valida AC-03 end-to-end.
- [ ] `ComprasControllerTests`: `POST /api/compras/confirmar` sin ningún cartón agregado (carrito
  vacío) → 400 `CarritoVacio` — valida AC-02 end-to-end.
- [ ] `ComprasControllerTests`: agregar un cartón al carrito, luego borrar directamente su clave
  `reservado:carton:{cartonId}` de Redis (conexión directa, mismo mecanismo que `CarritoRepositoryTests`
  — simula una reserva vencida sin esperar los 5 minutos reales) y confirmar → 409
  `ReservaCarritoInvalida` con el `cartonId` afectado en `cartonIdsInvalidos` — valida AC-07 end-to-end.
- [ ] `ComprasControllerTests`: `POST /api/compras/confirmar` sin autenticación (sin cookie
  `bingocart_auth`) → 401 — valida AC-01 end-to-end.
- [ ] `ComprasControllerTests`: `POST /api/compras/confirmar` autenticado como **organizador** (rol
  `Organizador`, no `Comprador`) → 403 — valida que `[Authorize(Roles = "Comprador")]` efectivamente
  distingue roles, no solo "cualquiera autenticado".
- [ ] `ComprasControllerTests` (concurrencia real, dos `HttpClient` con cookies independientes): dos
  compradores agregan el MISMO cartón cada uno a su propio carrito (mismo patrón de FEAT-008b, la
  segunda reserva ya falla en `IntentarAgregarAsync` con 409 — así que para forzar el caso de
  confirmación concurrente real, el test agrega el cartón normalmente con una sesión y simula la
  expiración de reserva del otro lado, o confirma dos carritos que NO se solapan pero ejercita
  `CrearVariasAsync` con `Task.WhenAll` sobre compras de organizadores distintos que sí pueden
  correr en paralelo sin conflicto) → ambas confirmaciones exitosas, sin deadlock ni excepción
  espuria — valida que la transacción de `CrearVariasAsync` no serializa innecesariamente compras
  no conflictivas.
- [ ] `ComprasControllerTests`: 61 requests consecutivas a `POST /api/compras/confirmar` (aunque
  fallen por carrito vacío, cuentan para el rate limit) desde el mismo comprador autenticado dentro
  de 5 minutos → el request 11 (límite 10) devuelve 429 — valida NFR-02 vía la política `"compras"`.

**Completion criterion**
Los 11 tests pasan; `docker-compose up --build` desde un clone limpio sigue levantando sin
intervención manual (roles sembrados automáticamente); un comprador puede registrarse, loguearse y
confirmar una compra de cartones de uno o varios organizadores, con el carrito vaciándose solo tras
persistir exitosamente en SQL Server.

## Final verification

`dotnet build`/`dotnet test` sobre `backend/BingoCart.sln` en verde, incluyendo los 38 tests
automatizados nuevos de los Blocks 1-3 (16+11+11) más la actualización del test de
`TieneComprasRegistradasAsync` (ya contado en Block 1) y el test de regresión de
`OrganizadorServiceTests` (ya contado en Block 2). Un comprador puede registrarse recién al momento
de comprar (nunca antes), confirmar cartones de uno o varios organizadores en compras
independientes, y ningún cartón queda disponible para otra compra tras confirmarse — verificado con
una restricción `UNIQUE` real en SQL Server, no solo con la reserva de Redis. Ningún frontend se
toca en este ticket (backend-only, mismo criterio que el resto del roadmap).
