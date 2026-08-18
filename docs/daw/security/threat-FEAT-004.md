# Threat Model FEAT-004: Listar bingos propios del organizador

| Field | Value |
|-------|-------|
| Ticket | FEAT-004 |
| PRD | docs/daw/prd/prd-FEAT-004.md |
| Date | 2026-08-18 |

## Componentes y superficies de ataque

| Componente | Nuevo/Modificado | Acepta input de usuario | Expone datos |
|---|---|---|---|
| `GET /api/bingos` (`BingosController.Listar`) | Nuevo | Sí — `page`, `pageSize` (query string) | Sí — bingos del organizador autenticado |
| `BingoService.ListarPropiosAsync` | Nuevo | No directamente (recibe `organizadorId` ya resuelto por el controller) | Sí |
| `BingoRepository.ListarPorOrganizadorAsync` | Nuevo | No directamente | Sí — vía consulta a `AppDbContext` |

## Trust boundaries

1. **Cliente HTTP (no confiable) → `BingosController`**: cruza con `[Authorize(AuthenticationSchemes
   = JwtBearerDefaults.AuthenticationScheme)]` (mismo mecanismo ya vigente de FEAT-001b/FEAT-003) +
   binding/validación de `page`/`pageSize` vía `[Range]` (400 automático en input inválido).
2. **`BingosController` → `BingoService` (Application, confiable)**: el controller nunca pasa
   `organizadorId` como viene del cliente — lo deriva exclusivamente del claim `NameIdentifier` del
   JWT ya validado.
3. **`BingoService` → `BingoRepository` (Infrastructure, confiable) → SQL Server**: consultas EF
   Core parametrizadas (LINQ), sin SQL crudo.

## STRIDE por componente

### `GET /api/bingos` (Api)

| Categoría | Evaluación |
|---|---|
| Spoofing | Mitigado — mismo mecanismo JWT/cookie httpOnly ya auditado en FEAT-001b/FEAT-003, sin cambios. |
| Tampering | `page`/`pageSize` son los únicos inputs; tipados y validados (`[Range(1, int.MaxValue)]`), sin superficie de tampering adicional. |
| Repudiation | N/A — GET de solo lectura, sin cambio de estado; mismo criterio que `GET /api/organizadores/perfil` (tampoco auditado). |
| **Information Disclosure** | **Riesgo real (ver tabla de riesgos, R-01).** |
| Denial of Service | `pageSize` sin acotar podría forzar payloads/consultas grandes — mitigado (ver R-02). |
| Elevation of Privilege | N/A — no hay niveles de privilegio distintos entre organizadores; cada uno solo ve lo propio. |

### `BingoService.ListarPropiosAsync` (Application)

| Categoría | Evaluación |
|---|---|
| Information Disclosure | Mismo riesgo R-01 — la mitigación en este componente es defensa en profundidad (clamping de `pageSize`, nunca confía en el valor crudo del caller). |
| Denial of Service | Clampea `pageSize` a 100 antes de tocar el repositorio — mitigación de R-02 vive acá. |
| Resto de categorías | N/A — sin I/O propio, sin autenticación propia. |

### `BingoRepository.ListarPorOrganizadorAsync` (Infrastructure)

| Categoría | Evaluación |
|---|---|
| Tampering / Injection | LINQ contra `AppDbContext`, parametrizado por EF Core — sin concatenación de queries. |
| Information Disclosure | El `Where(b => b.OrganizadorId == organizadorId)` es la última barrera antes de la DB — ver R-01. |

## Riesgos identificados

**R-01 (Information Disclosure) — Fuga de bingos entre organizadores (IDOR)**
| Campo | Valor |
|---|---|
| STRIDE | Information Disclosure |
| Likelihood | Low — el diseño deriva `organizadorId` exclusivamente del claim JWT en el controller, nunca de un parámetro de query/ruta/body; mismo patrón ya usado y probado en `POST /api/bingos` (FEAT-003). |
| Impact | High — expondría nombre de evento, fecha de sorteo, cantidad de cartones y costo de otro organizador (datos de negocio confidenciales, aunque no PII clásica). |
| Mitigación | `organizadorId` se resuelve una sola vez, en `BingosController.Listar`, del mismo claim `ClaimTypes.NameIdentifier` que ya usa `Crear` — nunca se acepta como parámetro. El filtro `Where(OrganizadorId == ...)` en `BingoRepository` es la barrera de datos. Test dedicado obligatorio (AC-04 del PRD): organizador A con bingos, organizador B autenticado no ve ninguno de A. |

**R-02 (Denial of Service) — Consultas o payloads sin acotar vía `pageSize`**
| Campo | Valor |
|---|---|
| STRIDE | Denial of Service |
| Likelihood | Low — requiere autenticación, y el resultado ya está acotado por la cantidad real de bingos del organizador (no hay amplificación posible: un organizador solo puede leer lo propio). |
| Impact | Low — en el peor caso, un payload más grande de lo esperado para un organizador con muchos bingos históricos; no hay amplificación cross-tenant. |
| Mitigación | `BingoService.ListarPropiosAsync` clampea `pageSize` a un máximo de 100 (FR-02/AC-06 del PRD) antes de invocar al repositorio — defensa en profundidad, no depende únicamente de la validación de modelo del DTO. |

**R-03 (Repudiation) — Sin logging de acceso al listado**
| Campo | Valor |
|---|---|
| STRIDE | Repudiation |
| Likelihood | N/A — riesgo aceptado |
| Impact | Low |
| Decisión | **Riesgo aceptado.** Un GET de solo lectura sobre datos propios no requiere auditoría — mismo criterio ya aplicado a `GET /api/organizadores/perfil` (sin logging dedicado). Aceptado por: equipo de desarrollo (vía este threat model). Justificación: no hay cambio de estado ni dato sensible de terceros involucrado; el riesgo de repudio solo aplica a acciones que alguien podría querer negar haber hecho, y "consultar mis propios datos" no es una de ellas. Revisar si en el futuro este endpoint se extiende para incluir datos de otros roles (ej. compradores) que sí ameriten trazabilidad. |

## Datos sensibles (clasificación)

`NombreEvento`, `FechaSorteoUtc`, `CantidadCartones`, `CostoPorCarton` — datos de negocio del
organizador, no PII ni credenciales. Ya persistidos con TDE habilitado en SQL Server (decisión de
FEAT-001a, sin cambios en este ticket). No se introduce ningún dato personal ni credencial nuevo.

## Mitigaciones a incorporar en la spec

1. `organizadorId` derivado exclusivamente del claim JWT en `BingosController.Listar`, nunca de
   input del cliente (Block 2).
2. `BingoService.ListarPropiosAsync` clampea `pageSize` a 100 como defensa en profundidad, no solo
   vía `[Range]` del DTO (Block 2).
3. Test obligatorio de aislamiento cross-organizador (AC-04 del PRD) — Block 2, `BingosControllerTests`.

---

**Total: R:0 C:0 H:0 M:0 L:2 (R-01, R-02 mitigados) + 1 riesgo aceptado (R-03).**
**Result: PASSED** → `gates.threat = true`.
