# Threat Model FEAT-003: Crear bingo con generación de cartones

| Field | Value |
|-------|-------|
| Ticket | FEAT-003 |
| Spec | docs/daw/specs/spec-FEAT-003.md |
| Date | 2026-08-17 |
| Result | PASSED (mitigaciones folded en el spec) |

## Attack surfaces identified

1. **`POST /api/bingos`** (Block 4) — endpoint protegido, acepta `nombreEvento`, `fechaSorteoUtc`,
   `cantidadCartones`, `costoPorCarton`. Dispara, en el camino feliz, la generación de hasta 5.000
   cartones (Block 2) y su persistencia (Block 3).
2. **`ICartonNumberGenerator`/`CartonNumberGenerator`** (Block 2) — CSPRNG, sin input de usuario
   directo más allá de la cantidad.
3. **Tablas `Bingos`/`Cartones`** (Block 3) — nueva superficie de persistencia, sin ningún endpoint
   de lectura todavía (fuera de alcance de este ticket).

## Trust boundaries

- Navegador (no confiable) → API (`POST /api/bingos`) — boundary existente desde FEAT-001a/b, ahora
  también recibe datos de creación de un recurso de negocio nuevo.
- API → SQL Server (`Bingos`/`Cartones`) — boundary existente, extendido con dos tablas nuevas.

## STRIDE — `POST /api/bingos`

| Categoría | Análisis |
|---|---|
| Spoofing | Mitigado: el dueño del bingo (`organizadorId`) se deriva del claim `NameIdentifier` del JWT, nunca del body — un organizador no puede crear un bingo a nombre de otro. |
| Tampering | HTTPS obligatorio (asunción de infraestructura ya declarada) cubre la integridad en tránsito. |
| Repudiation | 🟡 Ver Risk TM-02 — sin timestamp de creación, no queda registro de cuándo se creó el bingo. |
| Information Disclosure | Sin datos personales de terceros expuestos; `BingoCreadoResponse` no incluye los cartones (impracticable con 5.000, y ningún AC lo exige). |
| Denial of Service | 🟠 Ver Risk TM-01 — el camino costoso (generación de hasta 5.000 cartones) puede repetirse indefinidamente eligiendo una `fechaSorteoUtc` cercana. |
| Elevation of Privilege | N/A — un solo rol (organizador). |

## STRIDE — `ICartonNumberGenerator` (CSPRNG)

| Categoría | Análisis |
|---|---|
| Todas | Ya cubierto por NFR-02 del PRD (CSPRNG obligatorio, mitiga R-02 del PRD maestro: predictibilidad de números). Sin superficie nueva más allá de la ya contemplada en el PRD. |

## Risks

| # | Riesgo | STRIDE | Likelihood | Impact | Mitigación |
|---|--------|--------|------------|--------|------------|
| TM-01 | El chequeo de "bingo activo" (FR-06) evita el abuso CONCURRENTE del camino costoso (generación de hasta 5.000 cartones), pero NO evita que un organizador fije `fechaSorteoUtc` apenas en el futuro (ej. +1 minuto) y repita la creación cada vez que esa fecha vence, de forma indefinida — agotando CPU/DB con el tiempo, o simplemente inflando las tablas `Bingos`/`Cartones` sin límite. | DoS | Medium (requiere una cuenta de organizador válida, pero el registro no tiene una barrera fuerte contra automatización) | Medium (degradación de recursos, no caída total — cada ciclo cuesta &lt;10s por diseño de NFR-01) | **Mitigado**: rate limiter nuevo, particionado por `organizadorId` (no por IP, a diferencia de `"registro"` que es público), máximo 3 creaciones cada 5 minutos — folded en Block 4 del spec (`[EnableRateLimiting("bingos")]` + política nueva en `Program.cs`). |
| TM-02 | Sin un campo de timestamp de creación en `Bingo`, no hay forma de auditar cuándo se creó un bingo (solo se sabe su fecha de sorteo) — dificulta investigar abuso o reconstruir una cronología si algo sale mal. | Repudiation | Low (no es explotable por un atacante, es una carencia de auditoría) | Low | **Mitigado**: campo `Bingo.FechaCreacionUtc` agregado al agregado de dominio y a la tabla — folded en Block 1 y Block 3 del spec. |

## Sensitive data classification (F-TM-05)

- **Public/business data**: `NombreEvento`, `FechaSorteoUtc`, `CantidadCartones`,
  `CostoPorCarton` — ninguno es PII ni credencial. `OrganizadorId` ya está clasificado desde
  FEAT-001a/b (vincula al dueño, no expone datos personales nuevos).
- Sin datos de participantes/compradores en este ticket (ese flujo no existe todavía).

## Encryption (F-TM-07)

N/A — sin PII ni credenciales nuevas en el modelo de datos de este ticket.

## Nota informativa (no bloqueante)

`NombreEvento` se persiste tal cual lo envía el organizador, sin sanitización adicional (más allá
del límite de longitud). Este ticket no renderiza ese valor en ningún frontend (backend-only,
confirmado por el impact scan) — el riesgo de XSS solo se materializaría cuando un ticket futuro
lo muestre en una pantalla. Angular sanitiza por defecto en interpolación (`{{ }}`), así que el
riesgo real depende de que ese ticket futuro no use `innerHTML`/`bypassSecurityTrustHtml` sin
justificar — se deja constancia acá para que no se pierda de vista, no se folded ninguna mitigación
en este spec porque no hay nada que renderizar todavía.

## Mitigations folded into the spec

1. Block 4: rate limiter `"bingos"` particionado por `organizadorId`, 3 creaciones cada 5 minutos
   (TM-01).
2. Block 1 y Block 3: campo `Bingo.FechaCreacionUtc` (TM-02).

## Result

Risks: C:0 H:0 M:0 (2 mitigados: TM-01, TM-02) L:0 — ambos riesgos identificados fueron mitigados
por diseño antes de escribir el spec definitivo, no quedan como riesgos aceptados. **PASSED.**
