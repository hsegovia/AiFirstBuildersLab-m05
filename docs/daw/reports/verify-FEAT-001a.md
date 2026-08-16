# VERIFY FEAT-001a: Registro de organizador

| Field | Value |
|-------|-------|
| Ticket | FEAT-001a |
| PRD | docs/daw/prd/prd-FEAT-001a.md |
| Spec | docs/daw/specs/spec-FEAT-001a.md |
| Date | 2026-08-16 |

## Ronda 1 — BLOCKED

Verificación cruzada completa (`daw-module-verifier`) contra los 6 AC del PRD, los 7 bloques del
spec y el catálogo F-VER-01 a F-VER-06 / W-VER-01 a W-VER-03. Encontró 2 FAILs reales (ninguno una
regresión funcional — los 54 tests de entonces pasaban y los 6 AC estaban bien probados):

1. **F-VER-02 (Block 1)**: el threat model afirmaba TDE (cifrado at-rest) "Mitigado", pero nunca se
   implementó — solo un comentario en `docker-compose.yml` diciendo que no aplicaba en desarrollo
   local.
2. **F-VER-03 (Block 6, frontend)**: branch coverage 50% (mínimo 80%). Dos ramas reales sin test en
   `RegistroOrganizadorComponent` (`onSubmit()` con formulario inválido; `manejarError()` sin
   código de negocio) y `http-error.interceptor.ts` con 0% de cobertura, ni instrumentado.

**Decisión del usuario** sobre TDE: implementarlo de verdad (no reclasificar como riesgo aceptado).

## Corrective loop (VERIFY → CODE → VERIFY)

- `phase` → `CODE`, gates `tests`/`sast`/`verify` limpiados (`.daw-state.json`, entrada
  `2026-08-16T04:01:56Z`).
- **Fix TDE**: `src/BingoCart.Infrastructure/Data/AppDbContextTdeExtensions.cs` (nuevo) habilita
  master key, certificado y database encryption key sobre SQL Server, de forma idempotente.
  Invocado desde `Program.cs` tras `Database.MigrateAsync()` (gap adicional resuelto: no había
  auto-aplicación de migraciones al arrancar el contenedor `api`). Threat model y comentario de
  `docker-compose.yml` corregidos para reflejar la implementación real.
- **Fix cobertura frontend**: 2 tests nuevos en `registro-organizador.component.spec.ts` (las 2
  ramas señaladas) + `http-error.interceptor.spec.ts` (nuevo, 3 tests). Branch coverage 50% → 100%.
- Re-cierre de CODE: `dotnet test BingoCart.sln` (43/43) + `ng test --code-coverage` (18/18, 100%
  en las 4 métricas) + lint/format limpios + SAST re-corrido (sin hallazgos nuevos, documentado en
  `docs/daw/security/sast-FEAT-001a.md` § "Re-cierre").
- Commit `b45839a`, gates `tests`/`sast` re-earned, `phase` → `VERIFY`.

## Ronda 2 — PASS (con una nota de proceso, aceptada explícitamente)

Re-verificación completa e independiente (`daw-module-verifier`, sin confiar en nada de lo
reportado por los fixes): reprodujo el estado de la base desde cero (`docker compose down -v` → `up`),
confirmó `is_encrypted = 1` con consulta SQL directa, corrió las suites completas él mismo.

- **F-VER-01**: ✅ los 6 AC con test pasando. Suite .NET 43/43.
- **F-VER-02**: ✅ los 7 bloques completos, 21 tests del spec confirmados por nombre.
- **F-VER-03**: ✅ backend 97.1%/92.3%/97.9% (líneas/ramas/métodos, excluyendo `Migrations/`);
  frontend 100/100/100/100 confirmado por archivo (`registro-organizador.component.ts` y
  `http-error.interceptor.ts` ambos al 100%).
- **F-VER-04**: ✅ sad paths cubiertos (5 códigos de error del endpoint + no controlado +
  idempotencia de TDE).
- **F-VER-05**: ✅ `dotnet format --verify-no-changes`, `ng lint`, `tsc --noEmit` — limpios.
- **F-VER-06**: ✅ todos los tests del spec existen y pasan.
- **W-VER-01/02/03**: ✅ sin código muerto, cobertura de negocio >90%, y dos notas operativas no
  bloqueantes (ver abajo).

### Hallazgo de proceso: evidencia TDD formal ausente

El verificador señaló que no existe (todavía) un artefacto persistido con el detalle test-por-test
de "qué falló antes, con qué aserción" (Rule #-1 de `testing.instructions.md`) para el corrective
loop. Evaluado caso por caso:

- **TDE**: `AppDbContextTdeExtensions.cs` y su test son archivos enteramente nuevos — antes de este
  commit el test no compilaba (la forma más fuerte de "rojo" posible), confirmado por `git show
  b45839a --stat`.
- **Cobertura frontend**: el verificador confirmó que `registro-organizador.component.ts` y
  `http-error.interceptor.ts` **no cambiaron** en el commit del corrective loop — el código de
  producción ya era correcto; el gap era puramente de cobertura, no de comportamiento. La evidencia
  legítima acá no es una aserción rota, sino el reporte de cobertura antes/después (0%→100% en el
  interceptor, 50%→100% en branches del componente), que el implementador reportó y el verificador
  reconfirmó de forma independiente.

**Decisión explícita**: se acepta esta evidencia como suficiente para este corrective loop
puramente de cobertura sobre código ya verificado como correcto en bloques anteriores. No amerita
un tercer ciclo VERIFY↔CODE por una formalidad de reporte cuando la sustancia de ambos FAILs
originales ya fue verificada dos veces de forma independiente y en vivo.

### Notas operativas no bloqueantes (W-VER-03)

1. `AppDbContextTdeExtensionsTests` depende de estado de instancia de SQL Server (master key/
   certificado son objetos compartidos, no por-test) — la rama de "crear la master key" rara vez se
   re-ejercita en un entorno de desarrollo de larga vida; la rama de "ya existe" sí. Verificado que
   ambas ramas funcionan (la de creación, con el `down -v` completo). Documentado como limitación
   conocida, no bloqueante.
2. Correr la suite E2E completa repetidamente en una ventana corta contra el mismo contenedor `api`
   agota el rate limiter (5 req/min/IP, mitigación de Bloque 4) y produce timeouts falsos en
   Playwright — no es un bug, es la mitigación funcionando. Nota operativa para cualquier pipeline
   de CI que reintente la suite rápidamente contra el mismo entorno: esperar >60s entre corridas
   completas, o usar una instancia de `api` por corrida.

## Resultado final

```
✅ /daw-verify-module: PASSED (ronda 2, tras 1 corrective loop)
✅ Tests: 43 .NET + 18 Angular = 61 passed, 0 failed
✅ SAST (CODE phase): PASSED (2 CVEs .NET corregidas, 2 riesgos npm aceptados vía ADR-001)
```

**gates.verify = true.**
