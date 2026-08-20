# Verify Report — FEAT-008b (Carrito de compras)

| Field | Value |
|-------|-------|
| Ticket | FEAT-008b |
| Date | 2026-08-20 |
| Verifier | daw-module-verifier (agente independiente, no escribió el código) |
| Alcance | Verificación de INTEGRACIÓN completa del ticket (3 bloques) contra PRD + spec, no repetición de las revisiones por bloque ya hechas en CODE |
| Veredicto | **PASSED** (tras 1 corrective loop) |

## Corrective loop cerrado

La corrida inicial de esta verificación bloqueó por 2 FAILs (ver detalle abajo, conservado para
trazabilidad). Corrective loop VERIFY→CODE→VERIFY: se agregó el sad-path faltante
(`PedirNuevaTandaPorOrganizadorAsync_ConOrganizadorInexistente_...` en `CarritoServiceTests.cs` +
`NuevaTanda_ConOrganizadorIdInexistente_Devuelve404OrganizadorNoEncontrado` en
`CarritoControllerTests.cs`) y se corrigió el whitespace en `NuevaTandaRequest.cs:34`. Reverificado
de forma independiente: `dotnet format BingoCart.sln --verify-no-changes` → 0 errores;
`dotnet test BingoCart.sln --filter "FullyQualifiedName!~BingoCart.E2E.Tests"` → **194/194 PASS**
(192 previos + 2 nuevos). Ambos FAILs originales quedan cerrados sin reabrir ningún bloque de diseño.

## Trazabilidad PRD → Código → Tests

| AC | Resultado |
|---|---|
| AC-01 (FR-01, agregar sin registro/login) | ✅ PASS — `CarritoController.Agregar` → `CarritoService.AgregarAsync`. Unit (`CarritoServiceTests.AgregarAsync_ConCartonValidoYReservaExitosa_...`) + integración real (`CarritoControllerTests.Agregar_ConCartonRealSinCookiePrevia_Devuelve204YFijaLaCookieDeSesion`) |
| AC-02 (FR-02, sesión persistente en cookie) | ✅ PASS — `CarritoController.ObtenerOCrearSesionId`. Verificado que la respuesta fija `Set-Cookie: bingocart_carrito=...` en el mismo test que AC-01 |
| AC-03 (FR-03, nueva tanda sin repetir agregados/descartados) | ✅ PASS — `CarritoService.PrepararExclusionAsync`/`PedirNuevaTanda*Async`. Unit + integración real, ambos métodos |
| AC-04 (FR-04, carrito acumulado con cantidad/monto) | ✅ PASS — `CarritoService.ObtenerCarritoAsync`. Unit (3 ítems, 100/200/300 → total 600) + integración real |
| AC-05 (FR-05, quitar cartón) | ✅ PASS — `CarritoRepository.QuitarAsync`. Unit + integración real |
| AC-06 (FR-06/FR-07, reserva 5min reiniciada en cada agregado) | ✅ PASS — script Lua. TTL verificado con `KeyTimeToLiveAsync` contra Redis real |
| AC-07 (FR-08, liberación automática a los 5 min) | ✅ PASS — TTL de Redis, expiración real esperada en test |
| AC-08 (NFR-03, quitar no reinicia TTL de los restantes) | ✅ PASS — TTL antes/después con margen, contra Redis real |
| AC-09 (FR-10, cartón ya reservado rechazado) | ✅ PASS — dos `HttpClient`/`CookieContainer` independientes |
| AC-10 (FR-02, pérdida de sesión no recupera carrito) | ✅ PASS (indirecto pero suficiente) — no existe otra vía de recuperación que probar |

**Nota sobre AC-09/FR-10:** la mitad de "ya fue vendido" queda sin verificar porque `Compra` no
existe todavía en el dominio — decisión documentada explícitamente en el spec como implementación
parcial aceptada, no una reducción de alcance no autorizada.

## Spec — tareas por bloque

- ✅ Block 1: 9/9 tests requeridos, en verde, contra Redis real.
- ✅ Block 2: 10/10 tests requeridos, en verde. +1 test adicional del fix de SAST.
- ✅ Block 3: 10/10 tests requeridos, en verde. +3 tests adicionales (2 del fix de SAST, 1 de un
  hallazgo separado de la propia revisión de Block 3 sobre `[ValidateNever]`/body nulo).
- ⚠️ WARN: el fix de "Hallazgo 2" (`[ValidateNever]`) no quedó documentado como addendum en el spec
  (a diferencia del fix de SAST, que sí). No bloquea.
- ✅ Conteo total reconciliado: 33 tests dedicados a Carritos/extensiones (9+11+13).
- ⚠️ WARN: token de sesión en `Convert.ToBase64String` estándar (con `+`/`/`/`=`), no base64url como
  fija la spec — inofensivo (viaja en cookie, no en URL), pero desvío no documentado de una decisión
  cerrada en PLAN.

## Cobertura de sad-paths (F-VER-04)

| Endpoint | Sad-path cubierto |
|---|---|
| `POST /api/carrito/cartones/{cartonId}` | ✅ cartón inexistente → 404; ✅ ya reservado por otra sesión → 409 |
| `DELETE /api/carrito/cartones/{cartonId}` | ✅ nunca estuvo en el carrito → 204 idempotente |
| `GET /api/carrito` | N/A — sin input más allá de la cookie |
| `POST /api/carrito/tandas/nueva` | ✅ `cartonIdsDescartados` excede el límite → 400; ❌ **`organizadorId` inexistente → 404 (`OrganizadorNoEncontradoException`, código real en `CarritoService.PedirNuevaTandaPorOrganizadorAsync:130-134`) — CERO tests, ni unitarios ni de integración** |

❌ **FAIL — F-VER-04.** La rama "organizador inexistente" de `PedirNuevaTandaPorOrganizadorAsync` es
código nuevo de este ticket y no tiene ningún test que la ejerza.

## Cobertura aproximada (F-VER-03)

Sin collector formal, veredicto razonado sobre 33 tests dedicados + 192/192 de la suite completa:
`Carrito` (Domain) 100%; `CarritoRepository` (Infrastructure) las 5 operaciones cubiertas contra
Redis real, incluyendo concurrencia y expiración real; `CarritoService` con la única rama de negocio
nueva sin ejercitar (`PedirNuevaTandaPorOrganizadorAsync`, organizador inexistente).

⚠️ WARN — W-VER-02: `CarritoService.cs` por debajo del 90% recomendado de cobertura de lógica de
negocio, por la misma rama del FAIL de arriba.

## Calidad (F-VER-05)

- ✅ `dotnet build BingoCart.sln`: 0 Warning(s), 0 Error(s).
- ❌ **FAIL — `dotnet format BingoCart.sln --verify-no-changes`**: 1 error,
  `NuevaTandaRequest.cs:34` — doble espacio entre `[property: ValidateNever]` y
  `[param: ValidateNever]`. Trivial, no bloquea funcionalmente nada, pero la regla es explícita.

## Fragilidad de tests (W-VER-03)

- ✅ `BingoCart.Api.Tests` con `[assembly: CollectionBehavior(DisableTestParallelization = true)]`
  protege el test de rate-limit de interferencia cruzada.
- ✅ `CarritoRepositoryTests` genera `Guid.NewGuid()` propio por test — sin colisión posible entre
  corridas concurrentes. Confirmado empíricamente: 2 corridas completas, 0 fallas, 0 huérfanos.
- ⚠️ Sensibilidad a timing menor en el test de expiración de TTL (`Task.Delay(1300ms)` real) — sin
  fallas observadas en las corridas realizadas, riesgo bajo de flakiness futura bajo carga de CI.

## Confirmación empírica de aislamiento (Redis/SQL)

✅ Confirmado independientemente: dos corridas completas consecutivas de la suite, 192/192 PASS
ambas veces, `redis-cli DBSIZE` = 0 y `Bingos`/`Cartones`/`AspNetUsers` = 0 filas relacionadas antes
y después de cada corrida — sin claves Redis ni filas SQL huérfanas.

## Re-verificación de seguridad (independiente del SAST)

- ✅ Script Lua: `sesionId`/`cartonId`/`precioUnitario`/`ttlSegundos` exclusivamente vía `ARGV`.
- ✅ `ConstruirClausulaExclusion`: solo concatena `Guid.ToString()` sobre `IReadOnlyCollection<Guid>`
  tipado — sin ruta de string crudo de request.
- ✅ Cookie `bingocart_carrito`: `HttpOnly`/`Secure`/`SameSite=Strict` confirmados en el código real.

## Regresión E2E (confirmación propia)

- ✅ `RegistroOrganizadorE2ETests` corrido en aislamiento: 2/2 PASS en esta corrida (flakiness
  intermitente ya conocida, no una falla determinística).
- ✅ `git diff main...HEAD --stat -- backend/tests/BingoCart.E2E.Tests`: vacío — este ticket no tocó
  ningún archivo de E2E.

## Warnings (no bloqueantes)

1. AC-10 con test indirecto — arquitectónicamente suficiente, mejora opcional.
2. Fix "Hallazgo 2" sin addendum en el spec — mejora de trazabilidad, no bloquea.
3. Token de sesión en base64 estándar, no base64url — inofensivo, desvío no documentado.
4. W-VER-02: `PedirNuevaTandaPorOrganizadorAsync` bajo el 90% recomendado, misma causa que el FAIL.
5. Sensibilidad a timing en el test de expiración de TTL — riesgo bajo, no bloqueante.

## FAILs (bloqueantes)

1. **F-VER-04** — sin sad-path test para `organizadorId` inexistente en
   `POST /api/carrito/tandas/nueva`.
2. **F-VER-05** — `dotnet format BingoCart.sln --verify-no-changes` falla con 1 error de whitespace
   en `BingoCart.Api/Contracts/NuevaTandaRequest.cs:34`.

---

**Total: 10/10 AC (8 PASS + 2 PASS-con-nota) | Spec: 3/3 bloques completos (1 WARN de trazabilidad) | 0 FAIL (2 resueltos en corrective loop) | 6 WARN**
**Resultado: PASSED. 194/194 tests backend en verde, `dotnet format` limpio, sin claves Redis ni filas SQL huérfanas confirmado en dos corridas. Listo para RELEASE.**
