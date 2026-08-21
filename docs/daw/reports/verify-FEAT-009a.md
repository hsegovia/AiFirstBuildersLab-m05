# VERIFY — FEAT-009a (Confirmar compra, núcleo)

| Field | Value |
|-------|-------|
| Ticket | FEAT-009a |
| Date | 2026-08-21 |
| PRD | docs/daw/prd/prd-FEAT-009a.md |
| Spec | docs/daw/specs/spec-FEAT-009a.md |
| Threat model | docs/daw/security/threat-FEAT-009a.md |
| SAST | docs/daw/security/sast-FEAT-009a.md (PASSED) |
| Rondas | 1 (sin corrective loop en VERIFY) |

## Trazabilidad PRD → Código → Tests

- ✅ AC-01 (FR-01) → `ComprasController.Confirmar`, `[Authorize(Roles="Comprador")]` →
  `ComprasControllerTests.cs:362 Confirmar_SinAutenticacion_Devuelve401` (HTTP real, 401)
- ✅ AC-02 (FR-04) → `CompraService.ConfirmarCompraAsync:41-46` →
  `CompraServiceTests.cs:38` (unit) + `ComprasControllerTests.cs:322` (HTTP, 400 CarritoVacio)
- ✅ AC-03 (FR-05/06) → `CompraService.ConfirmarCompraAsync:77` (`GroupBy(OrganizadorId)`) →
  `CompraServiceTests.cs:86` + `ComprasControllerTests.cs:301` (HTTP, 2 organizadores → 2 CompraCreada)
- ✅ AC-04 (FR-02/06) → `Compra.Crear` + `CompraRepository.CrearVariasAsync` →
  `CompraRepositoryTests.cs:55` (persiste datos+Estado real, no solo status)
- ✅ AC-05 (FR-03) → mismo `CrearVariasAsync` → `CompraRepositoryTests.cs:55` (MedioPago
  Transferencia/Efectivo verificado)
- ✅ AC-06 (FR-07) → `BingoRepository.ObtenerParaCarritoAsync` + `DescubrimientoRepository`
  (`NOT EXISTS CompraCartones`) → `BingoRepositoryTests.cs:388` +
  `DescubrimientoRepositoryTests.cs:357,379`
- ✅ AC-07 (FR-08) → `CarritoRepository.RevalidarReservasAsync` + `CompraService:50-56` →
  `CarritoRepositoryTests.cs:253` + `CompraServiceTests.cs:57` + `ComprasControllerTests.cs:335`
  (HTTP real, 409 con `cartonIdsInvalidos`)
- ✅ AC-08 (FR-08/NFR-01) → PK `CompraCartones.CartonId` + `RevalidarReservasAsync` →
  `CarritoRepositoryTests.cs:208,322` + `CompraRepositoryTests.cs:84` +
  `ComprasControllerTests.cs:382`. **Nota de transparencia:** ningún test ejercita dos escrituras
  SQL verdaderamente simultáneas sobre el mismo `CartonId` vía `Task.WhenAll` — la garantía se
  prueba secuencialmente más la exclusividad estructural de Redis, exactamente como PLAN aprobó
  textualmente ("o confirma dos carritos que NO se solapan"). No es una desviación de CODE.
- ✅ AC-09 (FR-09) → `CompraService:106-108` (Liberar tras commit) → `CompraServiceTests.cs:145`
  (orden con `MockSequence`) + `ComprasControllerTests.cs:274` (HTTP real, GET carrito posterior
  vacío)

## Spec — tareas por bloque

- ✅ Block 1 (Domain+Infra): 16/16 tests requeridos presentes y en verde
- ✅ Block 2 (Application): 11/11 tests requeridos presentes y en verde
- ✅ Block 3 (Api): 11/11 tests requeridos presentes y en verde (incluye R-01 y rate-limit x2)

## Quality

- ✅ F-VER-05 Lint: `dotnet format BingoCart.sln --verify-no-changes` limpio. Build Release: 0
  warnings, 0 errors.
- ✅ F-VER-03 Coverage (medido con `dotnet test --collect:"XPlat Code Coverage"` +
  reportgenerator, contra el diff real `45ddc80..HEAD`): archivos nuevos 93.8% líneas / 88.5%
  ramas; archivos modificados 99.5% líneas / 81.2% ramas; combinado **96.7% líneas, 85.7% ramas**
  — ambos ≥80% con margen amplio.
- ✅ W-VER-01: sin dead code ni imports sin usar en archivos de este ticket.
- ✅ W-VER-03: sin tests frágiles — sin dependencias de orden, sin estado mutable compartido,
  CUITs/mails únicos por test, bases descartables por clase (Rule #0).

## Warnings (no bloquean, reportados para trazabilidad)

1. ⚠️ **W-VER-02**: `CompraService` en banda 80-90% (83.6% clase). `ConfirmarCompraAsync` en
   75.7% línea / 90% rama. Dos ramas sin test: `CompraService.cs:65-69` (cartón sin
   correspondencia, bingo eliminado entre agregar-al-carrito y confirmar) y
   `CompraService.cs:110-116` (el `catch (Exception)` que implementa la mitigación R-02 —
   fallo de `LiberarCarritoConfirmadoAsync` post-commit, logueado como warning, compra no se
   revierte). Recomendado antes de una futura iteración, no bloquea RELEASE.
2. ⚠️ `Comprador.Crear` (Domain) — 75% rama a nivel de método (94.7% línea a nivel de clase).
   Falta el caso "CUIT de longitud correcta pero dígito verificador inválido"
   (`Comprador.cs:43-45`) — asimetría con `OrganizadorTests.Crear_ConDigitoVerificadorInvalido_...`,
   que el spec dice que `Comprador` "espeja". Recomendado un test mirror.
3. ⚠️ `POST /api/compradores/login` sin test HTTP de credenciales inválidas → 401 en
   `CompradoresControllerTests.cs` (a diferencia de `/registro`, que sí cubre sus 3 sad-paths a
   nivel HTTP). Cubierto a nivel unitario y el patrón de traducción de excepción→401 ya está
   probado end-to-end para el organizador equivalente — riesgo bajo, no FAIL.
4. ⚠️ AC-08/NFR-01: ver nota de transparencia arriba (coincide con lo aprobado en PLAN).
5. ⚠️ Evidencia TDD no re-verificable en esta pasada (dato ya confirmado en las revisiones por
   bloque de CODE — `daw-implementer` reportó failing-before/passing-after por test en cada
   bloque, con re-confirmación independiente de `daw-arch-auditor`/`daw-module-verifier` en las
   rondas correctivas de Block 3).

## Re-verificación específica (diseño crítico)

- ✅ R-01 (threat model): `ComprasControllerTests.cs:372
  Confirmar_AutenticadoComoOrganizador_Devuelve403` — HTTP real con JWT de rol `Organizador` →
  403. Confirmado en verde.
- ✅ Orden CHECK→COMMIT→RELEASE: `CompraServiceTests.cs:145` (MockSequence, orden correcto) +
  `CompraServiceTests.cs:196` (Times.Never si `CrearVariasAsync` falla). Ambas direcciones de
  R-02 cubiertas.
- ✅ `CompraCartones.CartonId` como PRIMARY KEY (no solo UNIQUE): confirmado en
  `AppDbContext.cs:168` y en la migración real `20260820220612_AddComprasYComprador.cs:74`.

## Observación fuera de alcance

`BingoCart.E2E.Tests` (Playwright) tiene 1 falla preexistente,
`RegistroOrganizadorE2ETests.FlujoFeliz_...` (timeout, FEAT-001a, no tocado por el diff de
FEAT-009a). No bloquea este gate.

---

**Total: 20 passed, 0 failed, 5 warnings**
**Veredicto: PASSED**
