# Threat Model — FEAT-007 (Editar y eliminar bingo sin compras)

| Field | Value |
|-------|-------|
| Ticket | FEAT-007 |
| Date | 2026-08-19 |
| Spec | docs/daw/specs/spec-FEAT-007.md |

## Attack surfaces identified

1. `PUT /api/bingos/{id}` — recibe input de usuario (`NombreEvento`, `FechaSorteoUtc`,
   `CostoPorCarton`) y un identificador de recurso en la ruta (`id`) controlado por el atacante.
2. `DELETE /api/bingos/{id}` — mismo identificador de recurso en la ruta, operación destructiva e
   irreversible.
3. `BingoService.EditarAsync`/`EliminarAsync` — primer punto del sistema que carga una entidad por
   Id y decide si el solicitante puede operar sobre ella (autorización a nivel de objeto).
4. `Bingo.Actualizar` — primer método de mutación de una entidad de dominio hasta ahora inmutable.

## Trust boundaries

- **Api → Application**: el `id` de la ruta y el body llegan sin confianza; el único dato de
  identidad confiable es `organizadorId`, derivado exclusivamente de `ClaimTypes.NameIdentifier` del
  JWT (nunca de la ruta ni del body) — mismo patrón que `Crear`/`Listar`, sin excepción en este
  ticket.
- **Application → Infrastructure**: `BingoService` decide autorización (dueño, sin compras) antes de
  invocar al repositorio; `BingoRepository` no vuelve a autorizar, confía en el contrato del
  llamador — mismo criterio que el resto del proyecto.

## Risks

🔴 **CRITICAL: ninguno.**

🟠 **HIGH**

- **R-01 (Elevation of Privilege / Broken Object-Level Authorization — OWASP API1:2023):** un
  organizador autenticado podría editar o eliminar un bingo que no es suyo, simplemente conociendo o
  adivinando el `Guid` de otro organizador. **Mitigación ya incorporada en el diseño de la spec**:
  `BingoService.EditarAsync`/`EliminarAsync`, paso (2), compara `bingo.OrganizadorId` contra el
  `organizadorId` del JWT antes de cualquier operación — si no coincide, `BingoNoEncontradoException`
  (404), el mismo tipo y mensaje que "no existe". Verificado en el spec con tests e2e dedicados
  (dos organizadores reales, no un Id inventado). **Estado: mitigado por diseño, no requiere cambio
  adicional.**

🟡 **MEDIUM**

- **R-02 (Information Disclosure — enumeración de recursos):** si la respuesta distinguiera "no
  existe" (404 genérico) de "existe pero no es tuyo" (403, o un 404 con mensaje distinto), un
  atacante podría enumerar qué GUIDs de bingo son válidos aunque no pueda operarlos. **Mitigación ya
  incorporada**: ambos casos devuelven el mismo tipo de excepción (`BingoNoEncontradoException`) con
  el mismo mensaje ("El bingo indicado no existe.") — indistinguibles desde el cliente. **Estado:
  mitigado por diseño.**

🟢 **LOW**

- **R-03 (Denial of Service — sin rate limiting en PUT/DELETE):** un organizador autenticado podría
  spamear estos endpoints. Impacto bajo: son operaciones de una sola fila (`UPDATE`/`DELETE` por PK),
  sin el costo de `POST /api/bingos` (que genera hasta 5.000 cartones, sí rate-limiteado). Mismo
  criterio que `GET /api/bingos`, que tampoco tiene rate limiting por el mismo motivo. **Accepted
  risk**, consistente con el precedente ya establecido en el proyecto — no requiere mitigación nueva.
- **R-04 (Repudiation — sin auditoría de ediciones/eliminaciones):** no queda registro de quién
  editó o eliminó qué bingo más allá del log genérico de warning que ya emite
  `ExceptionHandlingMiddleware` para errores. Impacto bajo: el proyecto no tiene auditoría en ningún
  otro endpoint tampoco (ni `POST /api/bingos`, ni el registro de organizador). **Accepted risk**,
  fuera de alcance de este ticket — no es una regresión respecto al resto del sistema.
- **R-05 (Tampering — condición de carrera en ediciones concurrentes):** dos `PUT` concurrentes sobre
  el mismo bingo podrían resultar en "el último gana" sin detección de conflicto (sin control de
  concurrencia optimista). Impacto bajo: el recurso es de un único dueño (el organizador), no hay
  múltiples actores legítimos editando el mismo bingo simultáneamente — escenario de baja probabilidad
  real. **Accepted risk**, no se agrega `RowVersion`/`ConcurrencyToken` por ahora (evitar diseño para
  un requisito hipotético, coherente con las convenciones del proyecto).

## Sensitive data classification (F-TM-05)

`Bingo` (`NombreEvento`, `FechaSorteoUtc`, `CostoPorCarton`, `CantidadCartones`) — dato público del
negocio, no PII ni credenciales. `OrganizadorId` es una referencia interna, nunca se expone en el
response (`BingoCreadoResponse` no lo incluye). **Sin requisito de cifrado adicional (F-TM-07)**: no
hay PII ni credenciales en el flujo de este ticket.

## Mitigations folded into the spec

1. Autorización por objeto (ownership check) antes de cualquier edición/eliminación —
   `BingoService`, paso (2) de la orquestación (ya en la spec, Block 2).
2. No-enumeración: mismo tipo/mensaje de excepción para "no existe" y "no es tuyo" — `spec-FEAT-007.md`,
   sección "Decisiones cerradas en PLAN" (ya en la spec).
3. `organizadorId` derivado exclusivamente del JWT, nunca del `id` de ruta ni del body — mismo
   patrón que el resto del `BingosController` (ya en la spec, NFR-01).

Ningún riesgo CRITICAL/HIGH queda sin mitigar — R-01 (el único HIGH) ya está resuelto por el diseño
propuesto en la spec, sin requerir cambios adicionales.

---

**Risks: C:0 H:1 (mitigado) M:1 (mitigado) L:3 (accepted)**
**Veredicto: PASSED**
