# Threat Model — FEAT-008a (Descubrimiento de cartones)

| Field | Value |
|-------|-------|
| Ticket | FEAT-008a |
| Date | 2026-08-20 |
| Spec | docs/daw/specs/spec-FEAT-008a.md |

## Attack surfaces identified

1. `GET /api/cartones/descubrimiento` — público, sin input, primer endpoint del proyecto que
   ejecuta SQL crudo (`FromSqlInterpolated`) contra la base.
2. `GET /api/cartones/organizador/{organizadorId}` — público, recibe un `Guid` en la ruta.
3. `DescubrimientoRepository` — ejecuta `FromSqlInterpolated` con `ORDER BY NEWID()`, primer uso de
   SQL crudo en el proyecto (todo lo demás usa LINQ puro).
4. `DirectorioOrganizadorItem` (FEAT-005, extendido en este ticket) — ahora expone `Id`
   (organizadorId) en una respuesta pública.

## Trust boundaries

- **Api → Application**: `organizadorId` de la ruta no es confiable por sí solo — se valida su
  existencia (`ExisteOrganizadorAsync`) antes de usarlo para cualquier consulta downstream.
- **Application → Infrastructure**: la cantidad de cartones a pedir (`CantidadPorTanda = 5`) es una
  constante interna, nunca un parámetro controlado por el cliente — cierra cualquier vector de
  pedir una tanda arbitrariamente grande.

## Risks

🔴 **CRITICAL: ninguno.**

🟠 **HIGH: ninguno.**

🟡 **MEDIUM**

- **R-01 (Tampering — SQL injection vía `FromSqlInterpolated`):** primer uso de SQL crudo del
  proyecto es un cambio de superficie real, aunque `FromSqlInterpolated` parametriza
  automáticamente cada hueco interpolado (`{ahoraUtc}`, `{cantidad}`, `{bingoId}` se convierten en
  parámetros SQL, nunca en concatenación de string). **Mitigación:** ninguno de los valores
  interpolados en las 2 queries de este ticket proviene directamente de input del usuario sin pasar
  antes por una validación de tipo (`organizadorId:guid` en la ruta, rechazado por el routing de
  ASP.NET Core si no es un Guid válido; `cantidad` es la constante interna `CantidadPorTanda`,
  nunca un parámetro de request). SAST (CODE) debe confirmar explícitamente que ningún futuro
  cambio reintroduce concatenación de string en estas queries — es el precedente que el resto del
  proyecto no tenía hasta ahora.
- **R-02 (Information Disclosure — exponer `organizadorId` en el directorio):** el directorio
  público (FEAT-005) pasa a exponer el `Id` interno del organizador, que antes era opaco.
  **Mitigación:** un `organizadorId` es un identificador de recurso, no un secreto — ya se usa como
  tal en rutas autenticadas (`PUT`/`DELETE /api/bingos/{id}` exponen `Bingo.Id` de forma análoga).
  No habilita ninguna acción nueva: el único uso posible de ese `Id` sin autenticación es pedir sus
  cartones públicos (que ya eran visibles vía el Método 1 global). No reabre la mitigación de CUIT/
  mail/teléfono (R-01 del threat model de FEAT-005), que sigue intacta y con su test dedicado sin
  cambios.

🟢 **LOW**

- **R-03 (Denial of Service — scraping del stock completo de cartones):** un atacante podría llamar
  repetidamente a ambos endpoints para reconstruir el catálogo completo de cartones de un bingo.
  **Mitigación:** NFR-02 (rate limiting 60 req/5min por IP, política `"descubrimiento"`), mismo
  mecanismo ya validado en FEAT-005. Impacto real limitado: los cartones no tienen datos sensibles
  propios (solo números 1-90 y un `Id`), reconstruir el catálogo no compromete nada más allá de lo
  que el propio flujo de descubrimiento ya expone por diseño.
- **R-04 (predictibilidad de `ORDER BY NEWID()`):** `NEWID()` no es un generador criptográficamente
  seguro. **Accepted risk**, explícitamente fuera de alcance: RNF-07 del PRD maestro (CSPRNG
  obligatorio) aplica a la generación de los *números* de un cartón (dato que si fuera predecible
  permitiría fabricar cartones ganadores), no a *qué* cartones ya existentes se muestran en una
  tanda de descubrimiento — no hay ningún valor en predecir eso.

## Sensitive data classification (F-TM-05)

`Carton` (`Id`, `Numeros`) y `BingoResumen`/`CartonDescubiertoResponse` (`NombreOrganizacion`,
`NombreEvento`, `CostoPorCarton`, `FechaSorteoUtc`) — dato público del negocio, mismo nivel que el
directorio de FEAT-005. `organizadorId` ahora expuesto: identificador de recurso, no PII ni
credencial (F-TM-07 no aplica, sin requisito de cifrado adicional).

## Mitigations folded into the spec

1. `ExisteOrganizadorAsync` valida el `organizadorId` antes de cualquier consulta downstream (ya
   en la spec, Block 2).
2. `FromSqlInterpolated` (parametrizado automáticamente) en vez de `FromSqlRaw`/concatenación —
   ya en la spec, Block 1, sin alternativa considerada.
3. `CantidadPorTanda` como constante interna, nunca parámetro de request — ya en la spec, Block 2.
4. Rate limiting `"descubrimiento"` (60 req/5min/IP) en ambos endpoints — ya en la spec, Block 2.

Ningún riesgo CRITICAL/HIGH identificado. R-01 (MEDIUM) queda mitigado por diseño y debe
confirmarse explícitamente en el SAST de CODE, dado que es el primer SQL crudo del proyecto.

---

**Risks: C:0 H:0 M:2 (mitigados) L:2 (accepted)**
**Veredicto: PASSED**
