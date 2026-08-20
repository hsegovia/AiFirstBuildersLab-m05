# SAST Report — FEAT-008b (Carrito de compras)

| Field | Value |
|-------|-------|
| Ticket | FEAT-008b |
| Date | 2026-08-20 |
| Scope | Block 1 (Domain + Infraestructura Redis) + Block 2 (extensión de descubrimiento con exclusión + Application) + Block 3 (Api: `CarritoController`, cookie de sesión, rate limiting) + fix post-SAST (límite de `cartonIdsDescartados`) |
| Attempts | 2 (1 BLOCKED por F-SAST-14 MEDIUM, corregido, 2 PASSED) |

## Secrets (F-SAST-01)
✅ `docker-compose.yml` (nuevo servicio `redis`) y `appsettings.Development.json`
(`Redis:ConnectionString: "localhost:16379"`) — sin credenciales de ningún tipo (Redis se levanta
sin `requirepass`), mismo criterio ya aceptado para `MSSQL_SA_PASSWORD`.

## Injection — foco especial: primer uso de Redis y segunda instancia de SQL crudo del proyecto

- ✅ **F-SAST-02 aplicado a Lua (script `ScriptIntentarAgregar`, `CarritoRepository.cs:33-57`)**:
  `sesionId`/`cartonId`/`precioUnitario`/`ttlSegundos` entran exclusivamente vía `ARGV[1..4]`
  (`ScriptEvaluateAsync` con `RedisValue[]` separado del texto del script), nunca concatenados al
  cuerpo del script. La única concatenación de string dentro del script Lua (`'reservado:carton:' ..
  id`) usa un `id` devuelto por el propio `HKEYS` de Redis, nunca `sesionId` del cliente — sin forma
  de inyectar lógica Lua nueva. Verificado independientemente por dos revisores distintos (Block 1
  VERIFY y este SAST).
- ✅ **F-SAST-02 vía `FromSqlRaw` (`DescubrimientoRepository.ConstruirClausulaExclusion`)**:
  `cantidad`/`ahoraUtc`/`bingoId` siguen parametrizados de verdad (placeholders `{0}`/`{1}` propios
  de `FromSqlRaw`, traducidos a `SqlParameter`s reales por EF Core). La cláusula `NOT IN (...)` se
  arma concatenando texto en C#, pero exclusivamente a partir de `Guid.ToString()` sobre valores
  cuyo tipo (`IReadOnlyCollection<Guid>`, nunca `string`) el compilador de C# garantiza en cada punto
  de la cadena de llamadas — rastreado completo desde `CarritoController`/Redis hasta este método,
  sin ningún `string` crudo de request que llegue sin pasar antes por `Guid.Parse`/model binding.
  `Guid.ToString()` (formato "D") produce únicamente `[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-
  [0-9a-f]{4}-[0-9a-f]{12}` — verificado empíricamente (no solo por inspección) en Block 2 VERIFY,
  incluyendo un intento de `Guid.Parse` con payload de inyección clásico (`'; DROP TABLE...`), que
  lanza `FormatException` antes de poder convertirse a `Guid`. Reconfirmado en esta corrida sin
  encontrar ninguna relajación de tipo desde la última revisión. **No es una FAIL de "SQL injection
  (string concatenation)"**: la categoría existe para prevenir input de usuario sin sanitizar
  llegando a SQL, y acá el espacio de representación como string está cerrado por el sistema de
  tipos, no por una validación de negocio revocable.
- ✅ F-SAST-03 (command injection): sin `exec`/`Process.Start` en el alcance.
- ✅ `BingoRepository.ObtenerParaCarritoAsync` — LINQ puro, sin SQL crudo.

## Sesión anónima / criptografía (F-SAST-08)

- ✅ `CarritoController.ObtenerOCrearSesionId`: `RandomNumberGenerator.GetBytes(32)` — CSPRNG real
  (`System.Security.Cryptography`), no `System.Random` ni `Guid.NewGuid()`. 256 bits de entropía.
- ✅ Cookie `bingocart_carrito`: `HttpOnly = true`, `Secure = true`, `SameSite = Strict` — los 3
  flags confirmados en el código real, mismo nivel de estrictez que `bingocart_auth` (FEAT-001b).

## Validación de input (F-SAST-14) — hallazgo real, corregido en esta ronda

🟡→✅ **MEDIUM, RESUELTO: `NuevaTandaRequest.CartonIdsDescartados` sin límite de tamaño.** Primera
corrida de este SAST: el array alimentaba `ConstruirClausulaExclusion` (`NOT IN (...)` de SQL crudo)
sin ningún límite de tamaño de request (sin `MaxRequestBodySize` custom en `Program.cs`, default de
Kestrel ≈30 MB ≈750.000 GUIDs). Un único request de un cliente sin autenticar podía armar un `NOT IN`
de cientos de miles de literales, con degradación severa del optimizador de SQL Server compartido
por todos los participantes — riesgo de agotamiento de recursos (Denial of Service), no de
inyección (esa pregunta ya estaba cerrada, ver arriba). El rate limiting `"carrito"` mitiga la
*frecuencia*, no el *tamaño* de una request individual, así que no había compensating control real
para suprimir el hallazgo con el formato de 7 campos.

**Fix aplicado y verificado independientemente en la segunda corrida:**
- `CantidadDescartadosExcedeLimiteException` (nueva, `Domain/Carritos/Exceptions/`) → 400.
- `CarritoService.ValidarCantidadDescartados` (límite `MaxDescartadosPorRequest = 50`, 10x la tanda
  real de 5) se ejecuta como PRIMERA línea de `PedirNuevaTandaGlobalAsync`/
  `PedirNuevaTandaPorOrganizadorAsync` — confirmado que ninguna ruta llega a Redis/SQL sin pasar
  antes por la validación (test unitario con `VerifyNoOtherCalls()` sobre ambos mocks de
  repositorio, cero llamadas con 51 elementos).
- Caso borde verificado sin off-by-one: 51 elementos → 400; exactamente 50 → 200.
- `docs/daw/specs/spec-FEAT-008b.md` actualizado (corrección post-SAST documentada in situ).

## Error handling (F-SAST-15)

- ✅ `CartonInexistenteException`/`CartonYaReservadoException`/`CantidadDescartadosExcedeLimiteException`
  se mapean a 404/409/400 con mensajes de negocio controlados (sin stack trace, nombre de tabla ni
  connection string). El `catch (Exception ex)` genérico nunca expone `ex.Message` al cliente.

## XSS y funciones inseguras

- ✅ F-SAST-06: backend-only, sin render HTML. F-SAST-04/17: sin `eval()`/deserialización custom.

## Resto de categorías obligatorias

- ✅ F-SAST-05 (path traversal): no aplica.
- ✅ F-SAST-07 (SSRF): no aplica — `IConnectionMultiplexer` apunta a un connection string fijo de
  configuración, no a una URL controlada por el usuario.
- ✅ F-SAST-09 (debug mode): sin cambios de configuración de entorno.
- ✅ F-SAST-10 (logging de datos sensibles): sin ningún `_logger`/log nuevo en
  `CarritoRepository.cs`/`CarritoService.cs` — ni `sesionId` completo ni `cartonId` se loguean en
  texto plano.
- ✅ F-SAST-11 (upload sin restricciones): no aplica.
- 🟢 F-SAST-12 (CSRF), informational: los 3 endpoints de escritura dependen de la cookie
  `bingocart_carrito` (`SameSite=Strict`) para portar la sesión — `Strict` ya impide que el navegador
  la adjunte en un request cross-site, mitigación equivalente a un token CSRF explícito para este
  caso de uso (sesión anónima, sin flujo de navegación cross-site legítimo). Mismo criterio que
  `bingocart_auth` (FEAT-001b).

## Dependencias (F-SAST-13/16)
✅ `StackExchange.Redis 2.8.16` — `dotnet list package --vulnerable --include-transitive`: 0
vulnerabilidades conocidas.

## Riesgos del threat model (verificación de mitigaciones)

- ✅ R-01 (MEDIUM, `sesionId` sin firma): mitigación por entropía CSPRNG confirmada en código.
- ✅ R-02 (MEDIUM, Lua injection): confirmado descartado, ver Injection arriba.
- ✅ Addendum de threat model (`FromSqlRaw`/exclusión): confirmado sin riesgo de inyección, ver
  arriba.
- ✅ R-03/R-04/R-05 (LOW): sin cambios, ya aceptados con la misma justificación.
- Vector adicional evaluado y descartado (no un nuevo hallazgo): `CarritoRepository.
  IntentarAgregarAsync` hace `HKEYS`+`EXPIRE` por ítem ya presente en cada agregado — costo O(n) por
  request, pero `AgregarAsync` recibe un único `Guid` por request (no un array), así que acumular un
  `n` grande requiere `n` requests separados ya sujetos al rate limiting `"carrito"` (60/5min/IP) y
  acotados por la escasez real de cartones de un bingo (máx. 5.000, RF-03) — mismo criterio ya
  aplicado a R-03 (LOW, aceptado) en el threat model, no amerita reabrirlo.

## Suppressions
Ninguna aplicada — el único hallazgo Medium (F-SAST-14) se resolvió con un fix directo, no con
supresión.

---

**Total: 0 vulnerabilidades abiertas (0 Critical, 0 High, 0 Medium, 0 Low nuevo)**
**Hallazgos de esta ronda: 1 (MEDIUM, F-SAST-14) — resuelto y reverificado independientemente**
**Veredicto: PASSED**
