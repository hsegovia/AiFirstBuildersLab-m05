# PRD FEAT-003: Crear bingo con generación de cartones

| Field | Value |
|-------|-------|
| Ticket | FEAT-003 |
| Tracker | none |
| Date | 2026-08-17 |
| PRD loops | 0 |

## Context and Problem

Un organizador autenticado (FEAT-001a/FEAT-001b) todavía no tiene ninguna forma de publicar un
bingo. Sin esto, ningún otro requerimiento del organizador que depende de que exista un bingo
(listarlo, editarlo, que aparezca en el directorio público, vender cartones) es alcanzable.

Este ticket implementa exclusivamente la creación del bingo y la generación atómica de sus
cartones — RF-02, RF-03, RF-04a, RF-04b, RF-04c del PRD maestro
(`docs/daw/prd/prd-bingocartV2.md`). No implementa ningún listado, edición, ni el flujo de compra.

**Dos decisiones de alcance tomadas en esta fase DEFINE, no explícitas en el PRD maestro:**

1. **Un organizador solo puede tener un bingo activo a la vez** (el PRD maestro lo menciona en
   "Fuera de Alcance" como límite de producto, no como regla de negocio explícita — se decidió acá
   que sí se implementa como regla, ver FR-06/AC-05, para no dejar la puerta abierta a un estado
   inconsistente).
2. **Definición de "activo" para este ticket**: el PRD maestro define "activo" para el directorio
   público (RF-05) como "con stock disponible y fecha de sorteo vigente". Este ticket no depende
   del flujo de compra (que no existe todavía), así que ningún cartón puede estar vendido — la
   condición de stock es trivialmente siempre verdadera hoy. Por eso, para la regla de "un bingo
   activo a la vez" (FR-06), "activo" se define acá exclusivamente como **fecha y hora de sorteo no
   vencida**. Cuando exista el flujo de compra, un ticket futuro deberá incorporar la condición de
   stock a esta misma regla.

## Goals

| # | Objetivo | Métrica de éxito |
|---|----------|-------------------|
| G1 | Permitir a un organizador publicar un bingo con cartones en menos de 5 minutos (O1 del PRD maestro) | Un solo request de creación, sin pasos adicionales |
| G2 | Garantizar unicidad de cartones dentro de cada bingo | 0% de cartones con el mismo conjunto de números o el mismo GUID dentro de un mismo bingo |
| G3 | Generación de cartones con aleatoriedad criptográficamente segura | 100% de los números generados vía CSPRNG, 0 usos de un generador no criptográfico |

## Functional Requirements

- FR-01: El sistema debe permitir a un organizador autenticado crear un bingo indicando nombre del
  evento, fecha y hora del sorteo, cantidad de cartones y costo por cartón (en pesos argentinos,
  ARS).
- FR-02: El sistema debe rechazar la creación de un bingo cuya cantidad de cartones solicitada
  supere 5.000.
- FR-03: El sistema debe generar, al crear el bingo, tantos cartones como la cantidad indicada por
  el organizador, cada uno con 10 números aleatorios únicos entre 1 y 90, usando un generador de
  números pseudo-aleatorios criptográficamente seguro (CSPRNG).
- FR-04: El sistema debe asignar a cada cartón generado un GUID único que permita su identificación
  futura.
- FR-05: El sistema no debe generar, dentro de un mismo bingo, dos cartones con el mismo conjunto
  exacto de 10 números.
- FR-06: El sistema debe rechazar la creación de un nuevo bingo si el organizador ya tiene un bingo
  con fecha y hora de sorteo no vencida (ver Context and Problem, decisión 1 y 2).
- FR-07: El sistema debe rechazar la creación de un bingo si la fecha y hora del sorteo ya pasó, si
  la cantidad de cartones es cero o negativa, o si el costo por cartón es cero o negativo.

## Non-Functional Requirements

- NFR-01: La generación de hasta 5.000 cartones debe completarse en menos de 10 segundos (p95),
  medido desde que el organizador confirma la creación hasta que el bingo queda persistido con
  todos sus cartones.
- NFR-02: Los números de cada cartón deben generarse exclusivamente con
  `System.Security.Cryptography.RandomNumberGenerator` (CSPRNG de .NET Core 8, ver AGENTS.md); 0
  usos de `System.Random` u otro generador no criptográfico, verificable por revisión de código.

## Acceptance Criteria

- AC-01 (FR-01, FR-03): WHEN un organizador autenticado crea un bingo con nombre, fecha y hora de
  sorteo futura, una cantidad de cartones ≤ 5.000 y un costo por cartón válido, THE sistema SHALL
  crear el bingo y generar exactamente esa cantidad de cartones, cada uno con 10 números aleatorios
  únicos entre 1 y 90.
- AC-02 (FR-02): IF la cantidad de cartones solicitada supera 5.000, THEN THE sistema SHALL
  rechazar la creación del bingo indicando el límite máximo.
- AC-03 (FR-04): WHEN se generan N cartones para un bingo, THE sistema SHALL asignar un GUID único
  a cada uno, de forma que los N GUIDs sean todos distintos entre sí.
- AC-04 (FR-05): WHEN se generan N cartones para un bingo, THE sistema SHALL garantizar que no
  existan dos cartones con el mismo conjunto exacto de 10 números dentro de ese bingo.
- AC-05 (FR-06): IF el organizador ya tiene un bingo con fecha y hora de sorteo no vencida, THEN
  THE sistema SHALL rechazar la creación de un nuevo bingo indicando que ya tiene uno activo.
- AC-06 (FR-07): IF la fecha y hora del sorteo ya pasó, IF la cantidad de cartones es cero o
  negativa, o IF el costo por cartón es cero o negativo, THEN THE sistema SHALL rechazar la
  creación del bingo indicando el dato inválido.

## Out of Scope

- Listado de bingos propios del organizador ("Mis bingos", RF-02b) — ticket separado.
- Directorio público de organizadores con bingo activo (RF-05) — ticket separado; el bingo creado
  en este ticket no aparece en ningún listado todavía, solo queda persistido.
- Edición y eliminación de un bingo (RF-25, RF-26, RF-27) — ticket separado.
- Validación de un cartón por GUID (RF-06 del PRD maestro) — ticket separado.
- Dashboard del organizador (RF-22, RF-23, RF-24) — ticket separado, depende además del flujo de
  compra del Participante.
- Venta o selección de cartones por participantes, y todo el flujo de compra (RF-07 en adelante del
  PRD maestro) — no implementado todavía en la plataforma.
- Incorporar la condición de "stock disponible" a la definición de "bingo activo" (FR-06) — se
  hará cuando exista el flujo de compra; hasta entonces, "activo" se define solo por fecha de
  sorteo vigente (ver Context and Problem).

## Risks and Mitigations

| # | Riesgo | Impacto | Mitigación |
|---|--------|---------|------------|
| R-01 | Generación de números no aleatorios permitiría predecir cartones no vendidos (R-02 del PRD maestro). | Alto — fraude posible en el flujo de compra futuro. | CSPRNG obligatorio (NFR-02), sin excepciones, verificable por revisión de código. |
| R-02 | Generar 5.000 × 10 = 50.000 números en un solo request puede impactar performance o exceder el timeout HTTP (R-06 del PRD maestro). | Medio — timeout percibido como error por el organizador. | NFR-01 establece el SLA (<10s p95); la estrategia técnica concreta (bulk insert, batching) se define en PLAN. |
| R-03 | La definición de "bingo activo" (FR-06) solo considera fecha de sorteo, no stock, porque el flujo de compra no existe todavía — un ticket futuro que agregue compras debe recordar ampliar esta regla. | Bajo — hoy es la única definición posible; el riesgo es de deuda técnica olvidada, no de comportamiento incorrecto actual. | Documentado explícitamente acá y en el `Out of Scope`, para que quede trazable cuándo y por qué habrá que revisarlo. |

## Dependencies

Depende de **FEAT-001b** (login de organizador): requiere un organizador autenticado para crear un
bingo — el endpoint de este ticket queda protegido por el mismo mecanismo de autenticación
(JWT vía cookie httpOnly) que expone ese ticket. FEAT-001b ya está mergeado en `main` (PR #4,
mergeado el 2026-08-17) y la rama de este ticket
(`feat/FEAT-003-crear-bingo-generacion-cartones`) fue rebaseada sobre ese `main` actualizado antes
de continuar — la dependencia queda resuelta al momento de escribir este PRD.
