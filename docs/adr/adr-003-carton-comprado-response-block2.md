# ADR-003: `CartonCompradoResponse` y `CompraCreada.Cartones` no declarados en el spec de FEAT-009d Block 2

| Field | Value |
|-------|-------|
| Date | 2026-08-25 |
| Ticket | FEAT-009d |
| Status | Accepted |

## Context

El spec `docs/daw/specs/spec-FEAT-009d.md`, Block 2, exige en su tabla "API contract" (fila 3) que
`POST /api/compras/confirmar` incluya `NumeroCorrelativo` "en cada ítem de
`CartonParaConfirmarCompra`". La sección "Files" del bloque solo lista como tocados
`CartonParaConfirmarCompra.cs` y `BingoRepository.cs`.

Al implementar, `CartonParaConfirmarCompra` resultó ser un DTO **interno** de `Application` que
`BingoRepository` usa para calcular la compra — nunca se serializa al cliente. Antes de este bloque,
`ConfirmarCompraResponse` (vía `CompraCreada`) no exponía ningún desglose por cartón, solo
`CantidadCartones` (int). Cumplir la fila 3 tal como está escrita en el spec era, en los hechos,
imposible sin agregar una superficie nueva: no existía ningún campo por cartón en la respuesta HTTP
real que pudiera llevar el correlativo.

`daw-module-verifier`, al auditar el bloque, marcó esto como `FAIL` por archivos no declarados en el
spec (`CompraService.cs`, `CompraCreada.cs`, y el DTO nuevo `CartonCompradoResponse.cs`), aunque
confirmó que la implementación es funcionalmente correcta y está cubierta por tests reales
(`CompraServiceTests`, `ComprasControllerTests`). El spec no puede editarse en fase CODE
(prohibición absoluta del orchestrator), así que la corrección se registra aquí.

## Options considered

### Opción 1: Agregar `CartonCompradoResponse(Guid CartonId, int NumeroCorrelativo)` y
`CompraCreada.Cartones` para exponer el desglose que la fila 3 exige
- **Pros:** cumple el contrato ya aprobado (la tabla API contract del spec, no una funcionalidad
  nueva) con el mínimo campo necesario — solo id y correlativo, sin duplicar el detalle completo que
  corresponde al listado de "mis cartones" (Block 3).
- **Cons:** agrega un archivo y modifica dos más que el spec no listó explícitamente en Block 2.

### Opción 2: Dejar `CartonParaConfirmarCompra` como está y no exponer el correlativo en la
confirmación de compra
- **Pros:** cero archivos fuera de la lista del spec.
- **Cons:** incumple la fila 3 de "API contract" y AC-14 (mismo correlativo visible en las 4
  superficies del recorrido del comprador), que el spec ya tiene aprobado desde el loop 2 de DEFINE.

## Decision

Se adopta la **Opción 1**. La tabla "API contract" del spec ya era la fuente de verdad aprobada por
el usuario; el DTO que nombraba para llevar el campo estaba mal identificado (uno interno en lugar
del de respuesta HTTP), pero la obligación funcional —exponer el correlativo en la confirmación— no
cambia. `CartonCompradoResponse` es deliberadamente mínimo (solo `CartonId` y `NumeroCorrelativo`,
sin números, bingo ni estado de pago) para no adelantar el alcance del listado completo de Block 3.

## Consequences

- Archivos afectados, fuera de la lista original de "Files" del Block 2: `CompraService.cs` (arma
  `Cartones` al construir `CompraCreada`), `CompraCreada.cs` (agrega el parámetro
  `IReadOnlyList<CartonCompradoResponse> Cartones`), y el archivo nuevo
  `CartonCompradoResponse.cs`.
- El cambio es aditivo y compatible hacia atrás: ningún campo existente de `ConfirmarCompraResponse`
  se quitó ni renombró.
- Este ADR sirve como la corrección documental de la sección "Files" y de la fila 3 de "API
  contract" del spec de Block 2 — el spec en sí no se edita en CODE, pero cualquier lectura futura
  del spec debe cruzarse con este ADR para esos tres archivos.
- No se requiere volver a PLAN: la funcionalidad entregada es exactamente la que el spec ya exige en
  su tabla de contrato, solo con el DTO correcto para materializarla.
