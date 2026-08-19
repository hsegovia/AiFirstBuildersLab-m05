# Verify Report — FEAT-005 (Directorio público de organizadores)

| Field | Value |
|-------|-------|
| Ticket | FEAT-005 |
| Date | 2026-08-19 |
| Verifier | daw-module-verifier (agente independiente, no escribió el código) |
| Veredicto | **PASSED** |

## Trazabilidad PRD → Código → Tests

| AC | Requisito | Código | Test | Resultado |
|---|---|---|---|---|
| AC-01 | FR-01/02/04/05 — directorio con orden y campos correctos | `OrganizadoresController.Directorio` → `OrganizadorService.ListarDirectorioAsync` → `DirectorioRepository.ListarActivosAsync` | `Directorio_ConDosOrganizadoresRegistradosYUnBingoCadaUno_...OrdenadosPorFechaSorteoAscendente` (e2e real) | ✅ PASS |
| AC-02 | FR-02 — solo bingos con sorteo futuro | `DirectorioRepository.cs:29-31` (`Where(FechaSorteoUtc > ahoraUtc)`) | `ListarActivosAsync_ConOrganizadorCuyoUnicoBingoTieneFechaSorteoPasada_NoAparece` | ✅ PASS |
| AC-03 | FR-02 — sin organizadores activos | mismo camino | `Directorio_SinNingunOrganizadorConBingoActivo_Devuelve200ConItemsVacioYTotalCero` | ✅ PASS |
| AC-04 | FR-03/04 — paginación | `OrganizadorService` (cálculo `TotalPaginas`) | `Directorio_ConSieteOrganizadoresConBingoActivoSembrados_Page2PageSize5_...` | ✅ PASS |
| AC-05 | FR-03 — clamp de pageSize | `OrganizadorService.cs:93` (`Math.Min(pageSize, 100)`) | `ListarDirectorioAsync_ConPageSize500_InvocaAlRepositorioConPageSizeClampeadoA100` | ✅ PASS |
| AC-06 | FR-03 — page=0 → 400 | `[Range(1, int.MaxValue)]` en `ListarDirectorioQuery` | `Directorio_ConPageCero_Devuelve400DatosInvalidos` | ✅ PASS |
| AC-07 | FR-01 — endpoint público | `[AllowAnonymous]` | `Directorio_SinCookieDeAutenticacion_Devuelve200` | ✅ PASS |
| AC-08 | FR-06/NFR-02 — sin CUIT/mail/teléfono | `DirectorioOrganizadorItem` (3 campos) + `Select()` LINQ estricto | `Directorio_ConOrganizadorConCuitMailTelefono_LaRespuestaCrudaNoContieneEsosDatos` (body crudo) + `ListarActivosAsync_ProyeccionResultante_NuncaExponeCamposMasAllaDeLosTresDelDto` (estructural) | ✅ PASS (doble cobertura) |

## Spec — tareas por bloque

- ✅ Block 1 (índice + repositorio): 6/6 tests requeridos existen y pasan. Migración `20260819012305_AddIndiceFechaSorteoBingos` presente, índice `HasIndex(FechaSorteoUtc)` confirmado y ejercitado.
- ✅ Block 2 (Application + Api): 9/9 tests requeridos existen y pasan (el spec dice "10" en su completion criterion pero enumera 9 bullets — inconsistencia de redacción del spec, ya señalada en CODE, sin gap de cobertura real).
- ✅ Rate limiting `"directorio"` (30 req/5min/IP): verificado con test real (request 31 → 429).

## Cobertura (F-VER-03)

Medida con coverlet sobre SQL Server real. Todo el código nuevo/modificado por encima del mínimo 80%:

| Componente | Líneas | Branches |
|---|---|---|
| `DirectorioRepository.ListarActivosAsync` | 100% | N/A (LINQ→SQL, sin branches IL) |
| `OrganizadorService.ListarDirectorioAsync` | 100% | 100% (2/2 — ternario `Total==0` ejercitado en ambos sentidos) |
| `OrganizadoresController.Directorio` | 100% | — |
| DTOs (`DirectorioPaginado`, `DirectorioOrganizadorItem`, `DirectorioResponse`, `ListarDirectorioQuery`) | 100% | — |
| Índice nuevo en `AppDbContext.OnModelCreating` | ejercitado (hits=2) | — |

## Sad paths (F-VER-04)
✅ `page=0` → 400 (`Directorio_ConPageCero_Devuelve400DatosInvalidos`). `pageSize` sin techo → clamp verificado a nivel unitario (argumento real recibido por el mock).

## Calidad (F-VER-05, W-VER-01, W-VER-03)
- ✅ `dotnet build BingoCart.sln`: 0 warnings, 0 errors.
- ✅ Sin imports sin usar, sin código muerto en los 11 archivos nuevos/modificados.
- ✅ Suite completa corrida de forma independiente por el verificador: 113/113 PASS (Domain 35, Application 15, Infrastructure 26, Api 37). 0 regresiones. (E2E excluido: requiere frontend levantado, sin cambios de frontend en este ticket.)

## Warnings (no bloqueantes)

1. **Evidencia TDD no disponible en el contexto del verificador** — el reporte del `daw-implementer` con el conteo de tests fallando antes de la implementación no se le pasó al agente de verificación. El estado final del código y los tests es consistente con lo que pedía el spec y todo pasa; se registra como advertencia de proceso, no de código.
2. **NFR-01 (p95 < 1s) sin test cuantitativo** — la estrategia (índice nuevo + `Skip`/`Take` a nivel SQL) está documentada y es razonable, pero a diferencia del test de login (que usa `Stopwatch`), no hay ninguna aserción de tiempo sobre `GET /api/organizadores/directorio`. No bloquea porque NFR-01 no está atado a ningún AC específico del PRD.
3. **`AssemblyInfo.cs` (`BingoCart.Api.Tests`) sigue con alcance amplio** — `DisableTestParallelization = true` a nivel de todo el ensamblado (37 tests), cuando solo el test de AC-03 del directorio necesita base global limpia. No genera fragilidad nueva confirmada (cada test class usa su propio `WebApplicationFactory`), pero ralentiza la suite completa innecesariamente. Deuda técnica ya señalada en CODE, persiste sin resolver — recomendado acotarla con `[Collection]` antes de que el proyecto sume más test classes de integración.

---

**Total: 8 PASS, 0 FAIL, 3 WARN**
**Resultado: PASSED**
