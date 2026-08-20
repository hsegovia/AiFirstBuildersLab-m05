# Threat Model FEAT-005: Directorio público de organizadores con evento activo

| Field | Value |
|-------|-------|
| Ticket | FEAT-005 |
| PRD | docs/daw/prd/prd-FEAT-005.md |
| Date | 2026-08-19 |

## Componentes y superficies de ataque

| Componente | Nuevo/Modificado | Acepta input de usuario | Expone datos |
|---|---|---|---|
| `GET /api/organizadores/directorio` (`OrganizadoresController.Directorio`) | Nuevo, **público** | Sí — `page`, `pageSize` (query string) | Sí — nombre de organización, evento activo, fecha de sorteo |
| `OrganizadorService.ListarDirectorioAsync` | Nuevo | No directamente | Sí |
| `DirectorioRepository.ListarActivosAsync` | Nuevo | No directamente | Sí — vía JOIN `Bingos` + `Users` (Identity) |

## Trust boundaries

1. **Cliente HTTP (no confiable, SIN autenticación) → `OrganizadoresController.Directorio`**: a
   diferencia de `GET /api/bingos` (FEAT-004), este endpoint no cruza ningún boundary de auth — es
   público por diseño (`[AllowAnonymous]`, mismo patrón que `registro`/`login`). El único control en
   este cruce es la validación de `page`/`pageSize`.
2. **`OrganizadoresController` → `OrganizadorService` (Application, confiable)**: sin cambios de
   confianza — parámetros ya validados por el binding.
3. **`OrganizadorService` → `DirectorioRepository` (Infrastructure, confiable) → SQL Server**: el
   punto más sensible de este ticket — hace un JOIN entre `Bingos` y la tabla de Identity
   (`AspNetUsers`, vía `AppDbContext.Users`), la primera vez que el código cruza esas dos tablas.

## STRIDE por componente

### `GET /api/organizadores/directorio` (Api)

| Categoría | Evaluación |
|---|---|
| Spoofing | N/A — endpoint público, no hay identidad que suplantar. |
| Tampering | `page`/`pageSize` tipados y validados (`[Range]`), sin superficie adicional. |
| Repudiation | N/A — GET de solo lectura sobre datos públicos, mismo criterio que el resto de GETs no auditados del proyecto. |
| **Information Disclosure** | **Riesgo real — ver R-01.** |
| **Denial of Service** | **Riesgo real — ver R-02.** Es el primer endpoint público SIN rate limiting evaluado en este proyecto donde el riesgo aplica (a diferencia de `GET /api/bingos`/`GET /api/organizadores/perfil`, que están detrás de auth). |
| Elevation of Privilege | N/A. |

### `DirectorioRepository.ListarActivosAsync` (Infrastructure)

| Categoría | Evaluación |
|---|---|
| Tampering / Injection | LINQ contra `AppDbContext`, parametrizado por EF Core — sin SQL crudo. |
| **Information Disclosure** | **Riesgo real — ver R-01.** Es el primer código del proyecto que lee `AppDbContext.Users` fuera de `IIdentityGateway`. |

## Riesgos identificados

**R-01 (Information Disclosure) — Filtración de datos personales del organizador vía el JOIN con
`Users`**
| Campo | Valor |
|---|---|
| STRIDE | Information Disclosure |
| Likelihood | Low — mitigado por diseño (ver mitigación), pero el vector es real: es la primera consulta que toca la tabla de Identity fuera de `IIdentityGateway`, sin la disciplina ya establecida de esa clase. |
| Impact | High — expondría CUIT, mail o teléfono del organizador en un endpoint público sin autenticación, violando NFR-02 de este PRD y RNF-09 del PRD maestro (Ley 25.326 de Protección de Datos Personales). |
| Mitigación | `DirectorioRepository.ListarActivosAsync` proyecta con un `Select()` LINQ tipado explícitamente a `DirectorioOrganizadorItem(NombreOrganizacion, NombreEvento, FechaSorteoUtc)` — nunca materializa ni retorna `ApplicationUser` completo. Al ser una proyección a un tipo con exactamente esos 3 parámetros posicionales, agregar un campo sensible por error requeriría un cambio explícito y visible en el tipo, no una fuga silenciosa. Test obligatorio (AC-08 del PRD): confirma ausencia de `cuit`/`mail`/`telefono` en el JSON de respuesta real. |

**R-02 (Denial of Service) — Scraping/enumeración masiva del directorio público**
| Campo | Valor |
|---|---|
| STRIDE | Denial of Service |
| Likelihood | Medium — endpoint público, sin fricción de autenticación, trivial de automatizar a diferencia de `POST /api/bingos` (que sí requiere una cuenta). |
| Impact | Low/Medium — no hay amplificación de costo como la generación de cartones (TM-01 de FEAT-003), pero sí carga sostenida sobre un JOIN de 2 tablas si se automatiza a escala, y facilita scraping competitivo de la base completa de organizadores activos. |
| Mitigación | Rate limiting por IP, nueva política `"directorio"` en `Program.cs` (mismo mecanismo ya usado por `"registro"`) — límite generoso (30 requests/5 min) para no afectar la navegación paginada legítima, pero acotar scraping sostenido. Se agrega en Block 2. |

**R-03 (Repudiation) — Sin logging de acceso al directorio**
| Campo | Valor |
|---|---|
| STRIDE | Repudiation |
| Likelihood | N/A — riesgo aceptado |
| Impact | Low |
| Decisión | **Riesgo aceptado.** Mismo criterio que FEAT-004 (R-03): un GET de solo lectura sobre datos ya públicos no requiere auditoría. Aceptado por: equipo de desarrollo (vía este threat model). Justificación: no hay cambio de estado, y los datos expuestos son intencionalmente públicos. Revisar si en el futuro este endpoint se usa como vector de reconocimiento previo a otro ataque (correlacionar con R-02). |

## Datos sensibles (clasificación)

`NombreOrganizacion`, `NombreEvento`, `FechaSorteoUtc` — datos de negocio ya destinados a ser
públicos por diseño (RF-05 del PRD maestro). `CUIT`, `mail`, `teléfono` del organizador — PII,
**explícitamente excluidos** de este endpoint (FR-06/R-01). Ya persistidos con TDE habilitado en SQL
Server (decisión de FEAT-001a, sin cambios en este ticket).

## Mitigaciones a incorporar en la spec

1. `DirectorioRepository.ListarActivosAsync` — proyección LINQ estricta a `DirectorioOrganizadorItem`
   (3 campos), nunca `ApplicationUser`/`Bingo` completos (Block 1).
2. Test dedicado de ausencia de PII en la respuesta del directorio — AC-08 (Block 2).
3. Rate limiting por IP en `GET /api/organizadores/directorio`, política `"directorio"` (30
   requests/5 min), mismo mecanismo que `"registro"` (Block 2).

---

**Total: C:0 H:0 (mitigados) M:0 L:2 (R-01, R-02 mitigados) + 1 riesgo aceptado (R-03).**
**Result: PASSED** → `gates.threat = true`.
