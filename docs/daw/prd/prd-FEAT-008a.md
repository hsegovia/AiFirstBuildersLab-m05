# PRD FEAT-008a: Descubrimiento de cartones

| Field | Value |
|-------|-------|
| Ticket | FEAT-008a |
| Tracker | none |
| Date | 2026-08-20 |
| PRD loops | 0 |

## Context and Problem

Hasta ahora la plataforma solo tiene funcionalidad del lado del organizador: registrarse, loguearse,
crear un bingo, listar/editar/eliminar sus propios bingos, y un directorio público de organizadores
con evento activo (FEAT-005). Ningún visitante puede todavía ver ni un solo cartón — el catálogo de
cartones generado en FEAT-003 es invisible fuera del organizador dueño.

Este ticket es el primer paso del lado del participante/comprador (RF-07 a RF-13c del PRD maestro,
`docs/daw/prd/prd-bingocartV2.md`): los dos métodos de búsqueda que el PRD maestro llama
"Descubrimiento" (RF-07) y "Por organizador" (RF-08). Es la mitad de solo lectura de un ticket más
grande que originalmente incluía también el carrito de compras — se partió en dos (FEAT-008a/b,
mismo patrón que FEAT-001a/b) porque el carrito agrega estado con Redis, sesión sin registro y
reserva temporal, mientras que el descubrimiento es enteramente de solo lectura y no requiere
ninguna infraestructura nueva.

## Goals

- Un visitante sin registrarse puede ver 5 cartones aleatorios de cualquier organizador con bingo
  activo (Método de búsqueda 1 — Descubrimiento, RF-07).
- Un visitante sin registrarse puede elegir un organizador (del directorio de FEAT-005) y ver 5
  cartones aleatorios de su bingo activo (Método de búsqueda 2 — Por organizador, RF-08).
- Ningún cartón de un bingo cuyo sorteo ya pasó se presenta como opción de compra.
- La selección es realmente aleatoria en cada solicitud, no cacheada ni determinística.

## Functional Requirements

- FR-01: El sistema debe presentar a un visitante sin autenticar 5 cartones aleatorios,
  provenientes de cualquier organizador con bingo activo (Método de búsqueda 1 — Descubrimiento).
- FR-02: El sistema debe presentar a un visitante sin autenticar, dado el identificador de un
  organizador con bingo activo, 5 cartones aleatorios de ese bingo (Método de búsqueda 2 — Por
  organizador).
- FR-03: El sistema debe excluir de ambos métodos de búsqueda los cartones de bingos cuya fecha de
  sorteo ya pasó (mismo criterio de "activo" que el directorio público, FEAT-005).
- FR-04: El sistema debe asegurar que los 5 cartones de una misma tanda sean distintos entre sí, sin
  repetidos.
- FR-05: El sistema debe devolver los cartones disponibles, aunque sean menos de 5, cuando el total
  de cartones elegibles (global o del organizador indicado) sea menor a 5.
- FR-06: El sistema debe rechazar una búsqueda "Por organizador" cuando el identificador de
  organizador no corresponde a ningún organizador registrado, y debe devolver una lista vacía cuando
  el organizador existe pero no tiene un bingo activo.
- FR-07: El sistema debe variar la selección de cartones entre solicitudes sucesivas al mismo método
  de búsqueda, sin cachear ni repetir el mismo resultado de forma determinística.

## Non-Functional Requirements

- NFR-01: La selección aleatoria de cartones debe resolverse a nivel de base de datos (sin cargar en
  memoria el conjunto completo de cartones candidatos de un bingo con hasta 5.000 cartones, límite
  de RF-03 del PRD maestro) para elegir los 5.
- NFR-02: El sistema debe limitar a 60 solicitudes de descubrimiento (ambos métodos combinados) por
  IP cada 5 minutos, mitigando el scraping masivo del stock de cartones de un endpoint público sin
  autenticación.

## Acceptance Criteria

- AC-01 (FR-01): WHEN un visitante sin autenticar solicita el descubrimiento global de cartones, THE
  sistema SHALL devolver 5 cartones aleatorios pertenecientes a bingos activos de cualquier
  organizador.
- AC-02 (FR-02): WHEN un visitante sin autenticar solicita cartones de un organizador con bingo
  activo, THE sistema SHALL devolver hasta 5 cartones aleatorios de ese bingo.
- AC-03 (FR-06): IF el identificador de organizador indicado no corresponde a ningún organizador
  registrado, THEN THE sistema SHALL rechazar la solicitud con 404.
- AC-04 (FR-06): IF el organizador indicado existe pero no tiene un bingo activo, THEN THE sistema
  SHALL devolver una lista vacía con 200.
- AC-05 (FR-05): IF el total de cartones elegibles (global o del organizador) es menor a 5, THEN THE
  sistema SHALL devolver todos los disponibles, sin error.
- AC-06 (FR-07): WHEN se solicita el mismo método de búsqueda dos veces seguidas con cartones
  elegibles suficientes, THE sistema SHALL poder devolver una selección distinta en cada solicitud
  (sin cachear el resultado anterior).
- AC-07 (FR-03): WHEN el sistema selecciona cartones para cualquiera de los dos métodos, THE sistema
  SHALL excluir los que pertenezcan a un bingo cuya fecha de sorteo ya pasó.
- AC-08 (FR-04): WHEN el sistema arma una tanda de cartones, THE sistema SHALL asegurar que ninguno
  se repita dentro de esa misma tanda.
- AC-09 (FR-01): IF no existe ningún bingo activo en toda la plataforma, THEN THE sistema SHALL
  devolver una lista vacía con 200 para el descubrimiento global.

## Out of Scope

- Agregar cartones presentados a un carrito de compras (RF-09 a RF-13c) — ticket FEAT-008b.
- Descartar cartones de la tanda actual y pedir una nueva sin repetir descartes anteriores (RF-10) —
  depende de la identificación de sesión que introduce FEAT-008b; este ticket no sostiene estado
  entre solicitudes sucesivas.
- Registro, login o cualquier dato personal del comprador (RF-14 en adelante).
- Cualquier pantalla de descubrimiento en el frontend — backend-only, mismo criterio que
  FEAT-003/FEAT-004/FEAT-005/FEAT-007.

## Risks and Mitigations

- **Riesgo:** un filtro de "activo" incorrecto podría exponer cartones de bingos con sorteo ya
  pasado, o de organizadores sin bingo vigente. **Mitigación:** reutilizar exactamente el mismo
  criterio ya validado en FEAT-005 (`FechaSorteoUtc > ahoraUtc`), sin reinventar la condición.
- **Riesgo:** seleccionar 5 cartones aleatorios sobre un bingo de hasta 5.000 cargando todos en
  memoria degradaría el rendimiento y sería vulnerable a DoS. **Mitigación:** NFR-01 exige
  resolución a nivel de base de datos; el mecanismo concreto (ej. `TABLESAMPLE`, `ORDER BY NEWID()`
  con `TOP`, u otro) se decide en PLAN.
- **Riesgo:** un endpoint público sin autenticación es un objetivo natural de scraping para mapear
  todo el stock de cartones de un organizador. **Mitigación:** NFR-02 (rate limiting por IP), mismo
  patrón que el directorio público de FEAT-005.

## Dependencies

- `Bingo`/`Carton` (Domain, FEAT-003).
- Criterio de "bingo activo" (`FechaSorteoUtc > ahoraUtc`), mismo usado en el directorio público
  (FEAT-005) y en la edición/eliminación de bingo (FEAT-007).
- El directorio público de organizadores (FEAT-005) es el flujo previo natural para el Método 2 (el
  visitante elige un organizador de esa lista), aunque este ticket no depende técnicamente de él —
  solo recibe un identificador de organizador.
