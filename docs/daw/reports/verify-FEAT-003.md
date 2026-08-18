# VERIFY FEAT-003: Crear bingo con generación de cartones

| Field | Value |
|-------|-------|
| Ticket | FEAT-003 |
| PRD | docs/daw/prd/prd-FEAT-003.md |
| Spec | docs/daw/specs/spec-FEAT-003.md |
| Threat model | docs/daw/security/threat-FEAT-003.md |
| Date | 2026-08-18 |

## Resultado

`daw-module-verifier` — **PASSED** (0 FAILs, 2 WARNs no bloqueantes, 19 checks PASS).

### Trazabilidad PRD → Código → Tests

| AC | Cubierto por | Tests |
|---|---|---|
| AC-01 (FR-01, FR-03) | `Bingo.Crear`, `BingoService.CrearAsync` | `BingoTests`, `BingoServiceTests`, `BingosControllerTests.Crear_ConDatosValidos_...` |
| AC-02 (FR-02) | `Bingo.Crear` (límite 5.000) | `BingoTests`, `BingosControllerTests.Crear_ConCantidadCartonesExcedeLimite_...` |
| AC-03 (FR-04, GUID único) | `Carton.Crear` | `BingosControllerTests` — `Distinct(Id).Count()==100` explícito |
| AC-04 (FR-05) | `CartonNumberGenerator` (HashSet) + índice único `(BingoId, Numeros)` | `CartonNumberGeneratorTests`, `BingoRepositoryTests`, `BingosControllerTests.Crear_Con5000Cartones_...` |
| AC-05 (FR-06) | `BingoService.CrearAsync` (check barato antes de generar) | `BingoServiceTests` (verifica que el generador NO se invoca), `BingosControllerTests.Crear_ConBingoVigenteExistente_...` |
| AC-06 (FR-07) | `Bingo.Crear` (3 validaciones) | `BingoTests` (los 3 casos a nivel de dominio), `BingosControllerTests.Crear_ConFechaSorteoPasada_...` (representativo end-to-end, decisión de spec) |

### Spec — bloques y tests requeridos

- ✅ Block 1 (Dominio): 10/10 tests requeridos.
- ✅ Block 2 (Generador CSPRNG): 3/3 automatizados + inspección de código (0 uso de `System.Random`
  en todo el backend).
- ✅ Block 3 (Persistencia EF Core): 5/5 tests requeridos, migración sin cambios de modelo
  pendientes.
- ✅ Block 4 (Application + Api): 9 tests cubren los 10 escenarios listados (2 consolidados en un
  solo método, variación admitida explícitamente por el spec).
- ✅ Ningún bloque parcial.

### Calidad

- `dotnet build BingoCart.sln`: 0 warnings, 0 errors.
- `dotnet format --verify-no-changes`: sin diffs.
- Sin código muerto, sin imports sin usar en los archivos nuevos/modificados de Bingos.
- Suite completa: 73/76 (Domain 35/35, Application 10/10, Infrastructure 15/15, Api 23/23; E2E 0/3
  por falta de frontend/API corriendo en :8000/:8080 — preexistente de FEAT-001b, no de este
  ticket). Subset FEAT-003: 27/27.

### Cobertura (medida con coverlet + reportgenerator, no estimada)

Todas las clases nuevas/modificadas de este ticket ≥80% líneas y ≥80% branches. La mayoría en
100%/100% (`Bingo`, `Carton`, las 6 excepciones de dominio, `BingoService`, DTOs, `BingoRepository`,
`BingosController`). Dos casos en el mínimo aceptable, ambos con justificación explícita en el spec:

- ⚠️ `CartonNumberGenerator.cs`: 84.6% líneas / 80.0% branches — la rama sin cubrir es el reintento
  ante colisión (spec Block 2: probabilidad ~1 en 5,7×10¹², fuera de alcance de test por diseño).
- ⚠️ `ExceptionHandlingMiddleware.cs`: 2 de los 5 mapeos nuevos (`CantidadCartonesInvalidaException`,
  `CostoPorCartonInvalidoException`) sin cobertura directa de integración — cubiertos a nivel de
  dominio, decisión de spec Block 4 para no duplicar el mismo mecanismo de mapeo ya probado por los
  otros 3 casos. Efecto colateral: un error de copy-paste en esos dos códigos HTTP específicos no
  sería detectado por ningún test. No bloqueante, queda documentado para un ticket futuro que toque
  ese archivo.

```
✅ /daw-verify-module: PASSED
✅ Tests: 73 passed (backend, excl. E2E preexistente), 0 failed sobre código de este ticket
✅ SAST (CODE phase): PASSED (0 vulnerabilidades)
```

**gates.verify = true.**
