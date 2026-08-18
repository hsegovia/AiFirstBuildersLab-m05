# PRD FEAT-004: Listar bingos propios del organizador

| Field | Value |
|-------|-------|
| Ticket | FEAT-004 |
| Tracker | none |
| Date | 2026-08-18 |
| PRD loops | 0 |

## Context and Problem

FEAT-003 le permite a un organizador autenticado crear un bingo (`POST /api/bingos`), pero no existe
ninguna forma de que ese organizador vea los bingos que ya creó. Hoy la única evidencia de un bingo
creado es la respuesta 201 del momento de creación — si el organizador cierra esa pantalla, no tiene
manera de consultar sus propios bingos (RF-02b del PRD maestro, `prd-bingocartV2.md`, AC-01d).

## Goals

- Que un organizador autenticado pueda listar todos los bingos que él mismo creó, con la
  información básica de cada uno (nombre del evento, fecha y hora del sorteo, cantidad de cartones,
  costo por cartón).
- Que el listado esté paginado desde el inicio, para no degradar con el tiempo a medida que un
  organizador acumula bingos históricos.
- Que un organizador nunca pueda ver, ni siquiera de forma indirecta (conteos, metadata), los bingos
  de otro organizador.

## Functional Requirements

- FR-01: El sistema debe permitir a un organizador autenticado listar los bingos que él mismo creó,
  vía `GET /api/bingos`, mostrando para cada uno: nombre del evento, fecha y hora del sorteo,
  cantidad de cartones y costo por cartón.
- FR-02: El sistema debe paginar el listado mediante los parámetros de query `page` (base 1,
  default 1) y `pageSize` (default 20, máximo 100), devolviendo junto al listado la cantidad total
  de bingos del organizador y la cantidad total de páginas.
- FR-03: El sistema debe ordenar el listado por fecha de creación del bingo (`FechaCreacionUtc`)
  descendente — el bingo creado más recientemente aparece primero.
- FR-04: El sistema debe filtrar el listado exclusivamente por los bingos cuyo `OrganizadorId`
  coincide con el organizador autenticado (derivado del JWT, nunca de un parámetro de la request).
- FR-05: El sistema debe devolver una lista vacía (con status 200, no un error) cuando el
  organizador autenticado no tiene ningún bingo creado.

## Non-Functional Requirements

- NFR-01: El listado paginado debe responder en menos de 1 segundo p95, para cualquier `pageSize`
  dentro del máximo permitido (100), consultando contra el índice existente `(OrganizadorId)` en la
  tabla `Bingos` (agregado en FEAT-003, Block 3).
- NFR-02: El sistema debe garantizar que un organizador solo pueda acceder a sus propios bingos —
  0 accesos exitosos de un organizador a bingos de otro, verificado por pruebas de control de
  acceso (mismo criterio que RNF-04 del PRD maestro).

## Acceptance Criteria

- AC-01 (FR-01, FR-03): WHEN un organizador autenticado con bingos creados solicita
  `GET /api/bingos` sin parámetros de paginación, THE system SHALL responder 200 con la primera
  página (hasta 20 bingos) de sus propios bingos, cada uno con nombre del evento, fecha y hora del
  sorteo, cantidad de cartones y costo por cartón, ordenados por fecha de creación descendente.
- AC-02 (FR-05): IF el organizador autenticado no tiene ningún bingo creado, THEN THE system SHALL
  responder 200 con una lista vacía y el total en 0 — nunca un 404.
- AC-03 (NFR-02): IF la request no incluye autenticación válida (cookie JWT ausente, inválida o
  expirada), THEN THE system SHALL responder 401, sin ejecutar ninguna consulta a la base de datos.
- AC-04 (FR-04, NFR-02): WHEN el organizador A tiene bingos creados y el organizador B (autenticado,
  distinto de A) solicita `GET /api/bingos`, THE system SHALL devolver únicamente los bingos de B —
  ninguno de A, ni en el listado ni en el total.
- AC-05 (FR-02, FR-03): WHEN se solicita `GET /api/bingos?page=2&pageSize=5` y el organizador tiene
  más de 5 bingos, THE system SHALL devolver los bingos 6 a 10 (según el orden de FR-03) junto con
  el total real de bingos y de páginas.
- AC-06 (FR-02): IF `pageSize` solicitado supera 100, THEN THE system SHALL limitarlo a 100 (no
  rechazar la request con error).
- AC-07 (FR-02): IF `page` o `pageSize` son inválidos (cero, negativos, o no numéricos), THEN THE
  system SHALL responder 400 con un mensaje indicando el parámetro inválido.

## Out of Scope

- Editar o eliminar un bingo (RF-25/26/27) — ticket separado.
- Cantidad de cartones vendidos, estado de ventas o cualquier dato del dashboard del organizador
  (RF-22 a RF-24) — este listado muestra únicamente los datos propios del bingo (nombre, fecha de
  sorteo, cantidad total de cartones, costo), no datos derivados de ventas.
- Directorio público de organizadores con evento activo (RF-05) — ese endpoint es público y sin
  autenticación; este es privado y autenticado. Tickets distintos.
- Filtros por nombre, rango de fechas, o cualquier criterio de búsqueda dentro del listado propio.
- Ordenamiento configurable por el cliente (solo se soporta el orden fijo de FR-03).

## Risks and Mitigations

- **Riesgo:** un organizador con muchos bingos históricos degrada el tiempo de respuesta si no se
  pagina. **Mitigación:** paginación obligatoria desde el día uno (FR-02), con `pageSize` acotado a
  un máximo de 100 (AC-06).
- **Riesgo:** fuga de datos entre organizadores (ver el listado de otro organizador). **Mitigación:**
  el filtro por `OrganizadorId` se deriva siempre del claim `NameIdentifier` del JWT, nunca de un
  parámetro de la URL o del body — mismo patrón ya usado en `POST /api/bingos` (FEAT-003).

## Dependencies

- FEAT-003: agregado `Bingo`, tabla `Bingos` con índice `(OrganizadorId)`, y el mecanismo de
  autenticación JWT vía cookie httpOnly ya vigente (`[Authorize]`). Este ticket reutiliza esa
  infraestructura sin cambios — no agrega tablas ni migraciones nuevas.
