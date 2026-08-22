# PRD FEAT-009b: Mail de confirmación de compra con PDF adjunto y reintentos

| Field | Value |
|-------|-------|
| Ticket | FEAT-009b |
| Tracker | none |
| Date | 2026-08-21 |
| PRD loops | 0 |

## Context and Problem

FEAT-009a (ya en `main`) implementa la confirmación de compra: agrupa el carrito por organizador,
persiste una `Compra` por organizador y marca los cartones vendidos. Pero el comprador no recibe
ninguna confirmación por mail de lo que compró, ni ningún comprobante descargable de sus cartones —
el flujo termina en una respuesta HTTP que solo él ve en ese momento. Si pierde la sesión del
navegador (carrito anónimo, sin cuenta persistente todavía — FEAT-009d), no tiene ningún registro de
su compra.

Este ticket cierra ese hueco: envía un mail de confirmación con el detalle completo de la compra y
un PDF por cada cartón adquirido, de forma resiliente a fallas transitorias de SMTP.

## Goals

- El comprador recibe, sin acción de su parte, un mail que confirma su compra con el detalle
  completo de lo adquirido.
- Cada cartón comprado queda respaldado en un PDF adjunto, que el comprador puede guardar o
  imprimir.
- Una confirmación de carrito que generó varias `Compra` (una por organizador, FEAT-009a) se
  comunica en un ÚNICO mail, no uno por organizador.
- El envío del mail nunca compromete la respuesta de la confirmación de compra: la compra ya está
  hecha independientemente de si el mail se entrega o no.
- Una falla transitoria de SMTP no pierde el envío: se reintenta un número acotado de veces antes de
  darse por vencido.

## Functional Requirements

- FR-01: El sistema debe asignar un `ConfirmacionId` (GUID) compartido a todas las `Compra`
  generadas en una misma confirmación de carrito.
- FR-02: El sistema debe encolar un envío de mail de confirmación cada vez que se confirma
  exitosamente una compra, agrupando por `ConfirmacionId` todas las `Compra` de esa confirmación.
- FR-03: El sistema debe enviar, por cada confirmación de carrito, un único mail al comprador que
  detalle todas las compras generadas: nombre de la organización, ID de compra, monto total y los
  números de cada cartón, por cada compra.
- FR-04: El sistema debe adjuntar al mail de confirmación un archivo PDF por cada cartón comprado,
  mostrando los 10 números del cartón y su GUID.
- FR-05: El sistema debe reintentar el envío de un mail fallido hasta un máximo de 3 intentos
  totales, con 1 minuto de espera entre cada intento.
- FR-06: El sistema debe marcar un envío como "fallido" tras agotar los 3 intentos sin éxito, sin
  continuar reintentando.
- FR-07: El envío del mail de confirmación debe ser un proceso desacoplado de la respuesta HTTP de
  `POST /api/compras/confirmar` — la confirmación de compra nunca espera ni depende del resultado
  del envío.
- FR-08: El sistema debe persistir el estado de cada envío (pendiente, exitoso, fallido) y la
  cantidad de intentos ya realizados, de forma que sobreviva a un reinicio del proceso backend.

## Non-Functional Requirements

- NFR-01: El intervalo entre reintentos de envío debe ser de 1 minuto.
- NFR-02: El máximo de intentos de envío por mail debe ser 3.
- NFR-03: Ninguna librería nueva de background jobs (ej. Hangfire) se introduce — el mecanismo se
  implementa con `BackgroundService` de .NET más una tabla de outbox en SQL Server, reutilizando la
  infraestructura ya declarada en el stack (EF Core, MailKit, QuestPDF).

## Acceptance Criteria

- AC-01 (FR-01, FR-02): WHEN se confirma exitosamente una compra que generó uno o más registros de
  `Compra`, THE sistema SHALL encolar un único envío de mail de confirmación agrupando todas las
  compras de esa confirmación mediante un `ConfirmacionId` común.
- AC-02 (FR-03): WHEN el proceso de envío en background procesa un envío pendiente, THE sistema
  SHALL enviar al comprador un mail que detalla, para cada compra de esa confirmación, el nombre de
  organización, el ID de compra, el monto total y los números de cada cartón.
- AC-03 (FR-04): WHEN se envía el mail de confirmación, THE sistema SHALL adjuntar un archivo PDF
  por cada cartón comprado, mostrando sus 10 números y su GUID.
- AC-04 (FR-05): IF el envío de un mail falla, THEN THE sistema SHALL reintentarlo hasta un máximo
  de 3 intentos totales, con 1 minuto de espera entre cada intento.
- AC-05 (FR-06): IF se agotan los 3 intentos de envío sin éxito, THEN THE sistema SHALL marcar ese
  envío como "fallido" y no continuar reintentando.
- AC-06 (FR-07): WHEN se confirma una compra, THE sistema SHALL responder al comprador (HTTP 200)
  sin esperar a que el mail se envíe ni a que el proceso de envío concluya.
- AC-07 (FR-08): IF el proceso backend se reinicia mientras hay envíos pendientes o parcialmente
  reintentados, THEN THE sistema SHALL conservar el estado de esos envíos (pendiente, cantidad de
  intentos ya realizados) para continuar el ciclo de reintentos tras el reinicio.

## Out of Scope

- Confirmación/cancelación manual de pago por el organizador (RF-17c, RF-17d, RF-17e, RF-17f) —
  ticket FEAT-009c (pausado, depende de la infraestructura de mail que este ticket construye).
- Vista "mis cartones" del comprador, descarga de PDF bajo demanda pese a falla de mail (RF-20a,
  RF-20b), actualización de datos de cuenta (RF-21, RF-21b) — ticket FEAT-009d.
- Exponer el estado de un envío de mail (pendiente/exitoso/fallido) a través de cualquier endpoint
  de la API — se persiste internamente, pero no hay lectura pública en este ticket.
- Cualquier pantalla de checkout o confirmación en el frontend — backend-only, mismo criterio que el
  resto de este roadmap.
- Reintentos con backoff exponencial — se usa intervalo fijo de 1 minuto (decisión de PLAN,
  documentada como NFR-01).

## Risks and Mitigations

- **Primer uso de MailKit/SMTP y de QuestPDF en el proyecto**: sin precedente que auditar en el
  código existente. Mitigación: threat model dedicado en PLAN, con foco en no loguear el contenido
  del mail (puede contener datos personales del comprador) y en validar que las credenciales SMTP
  nunca queden hardcodeadas.
- **Reinicio del proceso backend a mitad de un ciclo de reintentos**: mitigado por diseño (FR-08,
  AC-07) — el estado vive en SQL Server, no en memoria, así que un reinicio no pierde el conteo de
  intentos ya realizados.
- **Un mail nunca se envía por una falla persistente de SMTP (ej. credenciales inválidas)**: los 3
  reintentos se agotan rápido (NFR-01: ~3 minutos) y el envío queda marcado "fallido" (FR-06) — no
  reintenta indefinidamente ni bloquea el procesamiento de otros envíos pendientes.

## Dependencies

- FEAT-009a (`Compra`, `ItemCompra`, `CompraCarton`, ya en `main`) — este ticket agrega la columna
  `ConfirmacionId` a la tabla `Compras` ya existente, vía una nueva migración de EF Core.
- MailKit y QuestPDF, ya declaradas en `AGENTS.md` ("Stack") pero sin ningún uso real en el código
  hasta este ticket.
