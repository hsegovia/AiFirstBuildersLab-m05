# PRD FEAT-005: Directorio público de organizadores con evento activo

| Field | Value |
|-------|-------|
| Ticket | FEAT-005 |
| Tracker | none |
| Date | 2026-08-19 |
| PRD loops | 0 |

## Context and Problem

Un participante no tiene hoy ninguna forma de descubrir qué organizadores tienen un bingo vigente
para comprar cartones — ni siquiera existe un endpoint público que liste organizadores (RF-05 del
PRD maestro, `prd-bingocartV2.md`, AC-16). FEAT-003/FEAT-004 solo cubrieron el lado del organizador
(crear y listar sus propios bingos); este ticket es el primer endpoint pensado para el participante,
sin requerir registro ni login.

## Goals

- Que cualquier visitante (sin autenticarse) pueda consultar qué organizadores tienen un bingo con
  sorteo vigente.
- Que el directorio nunca exponga datos personales del organizador (CUIT, mail, teléfono) — solo el
  nombre de la organización y el evento activo.
- Que el listado esté paginado desde el inicio, consistente con el criterio ya usado en FEAT-004.

## Functional Requirements

- FR-01: El sistema debe exponer un endpoint público (sin autenticación) que liste los
  organizadores con un bingo activo, vía `GET /api/organizadores/directorio`.
- FR-02: El sistema debe incluir en el directorio únicamente a los organizadores cuyo bingo tenga
  `FechaSorteoUtc` futura respecto al momento de la consulta — un organizador sin ningún bingo, o
  cuyo único bingo ya tuvo su sorteo, no aparece. (El chequeo de stock de cartones vendidos queda
  explícitamente diferido — ver "Out of Scope" — porque el sistema todavía no tiene ningún concepto
  de cartón vendido/reservado; se agrega cuando exista el flujo de compra.)
- FR-03: El sistema debe paginar el directorio mediante `page` (base 1, default 1) y `pageSize`
  (default 20, máximo 100), devolviendo junto al listado el total de organizadores activos y el
  total de páginas — mismo criterio que FEAT-004.
- FR-04: El sistema debe ordenar el directorio por `FechaSorteoUtc` ascendente — el bingo con sorteo
  más próximo aparece primero.
- FR-05: El sistema debe mostrar, por cada organizador activo, el nombre de la organización, el
  nombre del evento y la fecha y hora del sorteo de su bingo activo.
- FR-06: El sistema no debe exponer en el directorio ningún dato personal del organizador (CUIT,
  mail, teléfono) — solo el nombre de la organización.

## Non-Functional Requirements

- NFR-01: El directorio paginado debe responder en menos de 1 segundo p95, para cualquier
  `pageSize` dentro del máximo permitido (100).
- NFR-02: El endpoint no debe exponer datos personales del organizador (CUIT, mail, teléfono) — 0
  ocurrencias de esos campos en la respuesta, verificado por prueba dedicada (mismo criterio de
  protección de datos que RNF-09 del PRD maestro).

## Acceptance Criteria

- AC-01 (FR-01, FR-02, FR-04, FR-05): WHEN un visitante sin autenticar solicita
  `GET /api/organizadores/directorio` sin parámetros de paginación, THE system SHALL responder 200
  con la primera página (hasta 20 organizadores) que tienen un bingo con `FechaSorteoUtc` futura,
  mostrando nombre de la organización, nombre del evento y fecha de sorteo, ordenados por fecha de
  sorteo ascendente.
- AC-02 (FR-02): IF un organizador no tiene ningún bingo con `FechaSorteoUtc` futura (nunca creó
  uno, o su único bingo ya tuvo su sorteo), THEN THE system SHALL excluirlo del directorio.
- AC-03 (FR-02): WHEN no hay ningún organizador con evento activo, THE system SHALL responder 200
  con una lista vacía y el total en 0.
- AC-04 (FR-03, FR-04): WHEN se solicita `GET /api/organizadores/directorio?page=2&pageSize=5` y hay
  más de 5 organizadores activos, THE system SHALL devolver la página correspondiente junto con el
  total real de organizadores y de páginas.
- AC-05 (FR-03): IF `pageSize` solicitado supera 100, THEN THE system SHALL limitarlo a 100 (no
  rechazar la request con error).
- AC-06 (FR-03): IF `page` o `pageSize` son inválidos (cero, negativos, o no numéricos), THEN THE
  system SHALL responder 400 con un mensaje indicando el parámetro inválido.
- AC-07 (FR-01): WHEN un visitante sin cookie de autenticación (ni JWT) solicita el directorio, THE
  system SHALL responder 200 — el endpoint es público, no requiere login.
- AC-08 (FR-06): IF el organizador tiene CUIT, mail o teléfono registrados, THEN THE system SHALL
  excluir esos tres campos de la respuesta del directorio — únicamente expone el nombre de la
  organización.

## Out of Scope

- Chequeo de "stock disponible" (cartones sin vender) como parte de la condición de "activo" — el
  sistema no tiene todavía ningún concepto de cartón vendido/reservado (eso es parte del flujo de
  compra, RF-07 en adelante, no implementado). "Activo" en este ticket se define únicamente como
  "tiene un bingo con `FechaSorteoUtc` futura" — decisión explícita tomada en DEFINE. Cuando exista
  el flujo de compra, un ticket futuro agrega el chequeo de stock a este mismo endpoint.
- Página de detalle de un organizador o de su bingo (ver cartones disponibles, costo por cartón,
  etc.) — ticket futuro, parte del flujo de descubrimiento del participante (RF-07/RF-08).
- Búsqueda o filtro por nombre de organización o rango de fechas dentro del directorio.
- Costo por cartón como campo del directorio (decisión explícita en DEFINE: se muestra recién al
  entrar al detalle del organizador).
- Cualquier funcionalidad del flujo de compra (carrito, reserva, checkout) — RF-07 en adelante.

## Risks and Mitigations

- **Riesgo:** exponer datos personales del organizador (CUIT, mail, teléfono) en un endpoint público
  sin autenticación. **Mitigación:** FR-06/AC-08 — el directorio solo devuelve el nombre de la
  organización; el DTO de respuesta ni siquiera incluye esos campos (no es un filtro en runtime, es
  una ausencia estructural en el contrato).
- **Riesgo:** con muchos organizadores activos, un directorio sin paginar degrada el tiempo de
  respuesta. **Mitigación:** paginación obligatoria desde el día uno (FR-03), con `pageSize` acotado
  a un máximo de 100 (AC-05) — mismo criterio ya aplicado en FEAT-004.
- **Riesgo:** al no requerir autenticación, el endpoint es un blanco más fácil de scrapear
  masivamente. **Mitigación:** se evalúa en PLAN (threat modeling) si amerita rate limiting por IP,
  siguiendo el precedente de la política `"registro"` (endpoint público) de FEAT-001a.

## Dependencies

- FEAT-003: agregado `Bingo` (campo `FechaSorteoUtc`, `NombreEvento`), tabla `Bingos`.
- FEAT-001a: `NombreOrganizacion` del organizador, persistido en el registro (ASP.NET Core Identity,
  `ApplicationUser`) — este ticket lee ese campo, no lo modifica.
