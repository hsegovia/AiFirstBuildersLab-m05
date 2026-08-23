# Verify Report — FEAT-009c (Confirmación y cancelación manual de pago)

| Field | Value |
|-------|-------|
| Ticket | FEAT-009c |
| Date | 2026-08-22 |
| Verifier | daw-module-verifier (agente independiente, no escribió el código) |
| Alcance | Verificación de INTEGRACIÓN completa del ticket (3 bloques) contra PRD + spec, no repetición de las revisiones por bloque ya hechas en CODE |
| Veredicto ronda 1 | **BLOCKED** (2 FAIL) |

## Trazabilidad PRD → Código → Tests

| AC | Resultado |
|---|---|
| AC-01 (FR-01, confirmar pago propio) | ✅ PASS — `Compra.ConfirmarPago()` → `CompraTests`, `ComprasControllerTests.ConfirmarPago_ConCompraPropiaPendiente_Devuelve200YEstadoConfirmado`. Confirmado además contra la API real (Docker): compra pasa a "Confirmado" |
| AC-02 (FR-02, rechazo si no está pendiente) | ✅ PASS — `Compra.cs:81-84` + `CompraOrganizadorServiceTests` + `ComprasControllerTests.ConfirmarPago_ConCompraYaConfirmada_Devuelve409`. Confirmado manualmente: 2do intento → 409 |
| AC-03 (FR-03, cancelar propia pendiente) | ✅ PASS — `Compra.Cancelar()` → `ComprasControllerTests.Cancelar_ConCompraPropiaPendiente_Devuelve200YLiberaCartones` (E2E real contra SQL Server). Confirmado manualmente dos veces contra la API real |
| AC-04 (FR-04, rechazo al cancelar dos veces) | ✅ PASS — `CompraOrganizadorServiceTests.CancelarAsync_ConCompraYaCancelada_...`. Confirmado manualmente: 2do intento de cancelar → 409. ⚠️ Nota: no hay test de **controller** dedicado para "Cancelar ya cancelada → 409" (sí a nivel Domain/Application) — asimetría frente al mirror de ConfirmarPago, no exigido por el spec, no bloquea |
| AC-05 (FR-05, cartones liberados al cancelar) | ✅ PASS — 4 queries de disponibilidad (`BingoRepository`, `DescubrimientoRepository`) excluyen `Estado=Cancelado`, con tests unitarios + E2E. Verificado manualmente de punta a punta: el cartón reaparece en el discovery y el bingo cuya única compra fue cancelada vuelve a aceptar PUT/DELETE |
| AC-06 (FR-06, mail de cancelación) | ✅ PASS — `EnvioMailService.EncolarCancelacionAsync`/`ArmarMensajeCancelacion` con tests dedicados. Verificado de punta a punta contra smtp4dev real: mail recibido, sin adjuntos, cuerpo exacto esperado |
| AC-07 (FR-07, listado sin datos del comprador) | ✅ PASS — `CompraResumenResponse` (solo CompraId/Estado/MontoTotal/FechaCreacionUtc), test por reflection que asserta la lista exacta de propiedades. Confirmado manualmente: el JSON crudo nunca incluye PII del comprador |
| AC-08 (FR-08, 404 sin distinguir ajena/inexistente) | ✅ PASS — `ObtenerCompraPropiaAsync` (mismo `CompraNoEncontradaException` en ambos casos) + tests dedicados. Confirmado manualmente: mensaje idéntico en ambos casos |

## Spec — tareas por bloque

- ✅ Block 1 (Domain): 8/8 tests requeridos, en verde.
- ✅ Block 2 (Application): 13/13 tests requeridos, en verde. Incluye el MockSequence que prueba el orden Cancelar→GuardarCambios→EncolarCancelacion.
- ✅ Block 3 (Infrastructure + Api): 16/16 tests requeridos, en verde. Migración `20260822150224_AddTipoEnvioYCompraIdAEnviosMail` reversible, backfill correcto. `git diff main...HEAD --stat`: 0 archivos fuera de alcance.
- ✅ Corrección post-implementación documentada en el propio código: `ICompraRepository.ObtenerMontoTotalAsync` (evita depender de que la compra recién mutada caiga en la página 1 de `ListarPorOrganizadorAsync`), con su propio test dedicado.

## Cobertura (medida con `dotnet test --collect:"XPlat Code Coverage"` + `reportgenerator`, no estimación)

| Archivo | Líneas / Branches / Funciones |
|---|---|
| `Compra.cs`, `EnvioMail.cs`, excepciones nuevas, `CompraOrganizadorService.cs`, `EnvioMailService.cs`, `ComprasController.cs`, `BingoRepository.cs`, `DescubrimientoRepository.cs` | ✅ 100% / 100% / 100% |
| `ExceptionHandlingMiddleware.cs` | ⚠️ 94.3% / 92.0% — líneas sin cubrir son catches preexistentes (FEAT-003/FEAT-007), no tocados por este ticket. Los 2 catches nuevos de FEAT-009c están 100% cubiertos |
| `CompraRepository.ListarPorOrganizadorAsync` | ❌ **FAIL F-VER-03** — 100% líneas, **50% branches**. Falta ejercitar la rama `: 0m` de `TryGetValue(...) ? monto : 0m` (línea 100) — compra sin filas en `CompraCartones` |
| `EnvioMailRepository.ObtenerDatosParaCancelacionAsync` | ❌ **FAIL F-VER-03** — 88.9% líneas, **57.1% branches**. Rama sin cubrir: `comprador is null` (líneas 121-123) y las 3 ramas `??` de `Email`/`Nombre`/`Apellido` (línea 129) |

## Sad paths (verificados contra la API real en Docker)

- ✅ Compra ajena → 404 "CompraNoEncontrada" (confirmar-pago y cancelar).
- ✅ Compra en estado inválido → 409 "EstadoInvalido" (confirmar 2 veces, cancelar 2 veces).
- ✅ Sin rol Organizador → 403 (test automatizado).
- ✅ `page`/`pageSize` fuera de rango en `GET /api/compras/mias` → 400 "DatosInvalidos" (verificado manualmente; ⚠️ sin test automatizado propio de este ticket — funciona por reutilizar el mecanismo de `[Range]` de `ListarBingosQuery`).
- ⚠️ NFR-02 (rate limiting 30 req/5min, `compras-organizador`) — verificado manualmente contra la API real (30 OK, 31+ → 429, particiona por JWT). Sin test automatizado de regresión — la política hermana "compras" sí tiene uno (precedente de un bug real de partición en FEAT-008b).

## Calidad

- ✅ `dotnet format BingoCart.sln --verify-no-changes`: limpio.
- ✅ `dotnet build -warnaserror:CS8019,IDE0005`: 0 warnings, 0 errores.
- ✅ Sin código muerto ni tests frágiles detectados.

## Suite completa

- ✅ 298/298 tests backend en verde, confirmado en 2 corridas independientes (57 Domain + 69 Application + 84 Infrastructure + 88 Api).
- ⚠️ `BingoCart.E2E.Tests`: 2/3 en verde, 1 falla preexistente por timeout en el registro de organizador (FEAT-001a) — no relacionada con este ticket, ningún archivo del diff toca ese flujo.

## Verificación funcional end-to-end (Docker real, imagen reconstruida)

- ✅ Confirmar pago propio → Confirmado; 2do intento → 409.
- ✅ Cancelar pendiente propia → Cancelado; cartón reaparece en discovery.
- ✅ Mail de cancelación recibido en smtp4dev, sin adjuntos, dentro del ciclo del BackgroundService.
- ✅ Confirmar/cancelar ajena → 404, mismo mensaje que "no existe".
- ✅ Bingo con única compra cancelada vuelve a ser editable/eliminable.
- ✅ `GET /api/compras/mias` nunca expone datos del comprador.

## Warnings (no bloqueantes)

1. Asimetría: sin test de controller para "Cancelar ya cancelada → 409" (sí existe a nivel Domain/Application).
2. Sin test automatizado de `page`/`pageSize` fuera de rango en `GET /api/compras/mias` (funciona por mecanismo compartido, no por test propio).
3. Sin test automatizado de regresión para el rate limiting de `compras-organizador` (la política hermana "compras" sí lo tiene, con precedente de un bug real).

## FAILs (bloqueantes)

1. **F-VER-03** — `CompraRepository.ListarPorOrganizadorAsync`: branch coverage 50% (< 80%). Falta `ListarPorOrganizadorAsync_ConUnaCompraSinCartones_DevuelveMontoCero`.
2. **F-VER-03** — `EnvioMailRepository.ObtenerDatosParaCancelacionAsync`: branch coverage 57.1% (< 80%). Falta `ObtenerDatosParaCancelacionAsync_ConCompradorInexistente_DevuelveNull`.

---

**Total: 8/8 AC PASS | Spec: 3/3 bloques completos | 2 FAIL (branch coverage) | 3 WARN**
**Resultado ronda 1: BLOCKED. Ningún AC del PRD sin cubrir; toda la lógica de negocio central está probada y confirmada contra el stack real. Los 2 FAIL son ramas defensivas puntuales en 2 métodos nuevos, con el nombre del test recomendado ya identificado. Corrective loop VERIFY→CODE abierto.**

---

## Ronda 2 — re-verificación tras el corrective loop

| Field | Value |
|-------|-------|
| Motivo | Corrective loop cerrado en CODE: 4 tests nuevos (commit `14bb94b`) para cerrar F-VER-03 |
| Alcance | Re-verificación FOCALIZADA — no repite la trazabilidad AC→código→test ni la verificación E2E/Docker de ronda 1 (sin cambios, 0 archivos de producción en el diff del corrective loop) |
| Veredicto | **PASSED** |

Cobertura, medida de forma independiente por el verificador (`dotnet test --collect:"XPlat Code
Coverage"` + `reportgenerator`, no una repetición de la cifra del orquestador):

| Método | Antes | Ahora |
|---|---|---|
| `CompraRepository.ListarPorOrganizadorAsync` | 50% branches | ✅ 100% líneas / 100% branches |
| `EnvioMailRepository.ObtenerDatosParaCancelacionAsync` | 57.1% branches | ✅ 100% líneas / 100% branches |

Los 4 tests nuevos fueron leídos línea por línea contra el código de producción real y confirmados
como ejercitando la rama que decían cubrir (no solo "pasan"):
`ListarPorOrganizadorAsync_ConUnaCompraSinCartones_DevuelveMontoCero` (rama `: 0m` del
`TryGetValue`), `ObtenerDatosParaCancelacionAsync_ConCompradorInexistente_DevuelveNull` (rama
`comprador is null`), `ObtenerDatosParaCancelacionAsync_ConOrganizadorInexistente_UsaNombreOrganizacionVacio`
(rama `organizador?.NombreOrganizacion ?? string.Empty`),
`ObtenerDatosParaCancelacionAsync_ConCompradorSinEmailNombreNiApellido_DevuelveCadenasVacias` (las 3
ramas `??` restantes).

- ✅ Suite completa (excluyendo E2E), corrida propia del verificador: 302/302 en verde (57+69+88+88).
- ✅ `git show 14bb94b --stat`: 3 archivos exactos (2 archivos de test + el SAST actualizado), **0
  archivos de producción** — sin riesgo de regresión funcional silenciosa.
- ✅ SAST ronda 2 (mismo commit): PASSED, sin secrets/PII en los tests nuevos.
- ✅ `dotnet format BingoCart.sln --verify-no-changes`: limpio.
- ℹ️ Nota fuera de alcance: `EnvioMailRepository.ObtenerDatosParaEnviarAsync` (FEAT-009b, no tocado
  por este ticket) sigue en 58.3% branch coverage — no es un FAIL de este ticket, no estaba
  flagueado en ronda 1 y el diff de este ticket no lo toca.
- Pendientes heredados de ronda 1, sin cambios (WARN, no bloqueantes, no exigidos por el spec):
  sin test de controller para "Cancelar ya cancelada → 409"; sin test de `page`/`pageSize` fuera de
  rango en `GET /api/compras/mias`; sin test de regresión del rate limit de `compras-organizador`.

---

**Total ronda 2: 0 FAIL | 3 WARN (heredados, no bloqueantes) | 9 PASS**
**Resultado: PASSED. Listo para RELEASE.**
