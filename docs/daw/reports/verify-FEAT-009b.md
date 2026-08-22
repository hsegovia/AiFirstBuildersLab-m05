# Verify Report — FEAT-009b (Mail de confirmación de compra con PDF adjunto y reintentos)

| Field | Value |
|-------|-------|
| Ticket | FEAT-009b |
| Date | 2026-08-22 |
| PRD | docs/daw/prd/prd-FEAT-009b.md |
| Spec | docs/daw/specs/spec-FEAT-009b.md |
| Verifier | daw-module-verifier (agente independiente, no participó en CODE) |

## Acceptance criteria (PRD)

- ✅ **AC-01/AC-02** (FR-01/FR-02): `ConfirmacionId` único compartido entre `Compra` de organizadores
  distintos → un único `EnvioMail` encolado. `CompraService.cs:80`, `EnvioMailService.cs:38-43`.
  Tests: `CompraServiceTests.cs:243`, `EnvioMailServiceTests.cs:56`. **Reproducido en vivo**: compra
  real con cartones de 2 organizadores → 1 solo mensaje en smtp4dev.
- ✅ **AC-02** (FR-03): detalle completo (organización, ID compra, monto, números) por cada compra
  en el mismo mail. `EnvioMailService.cs:118-142`. Cuerpo HTML inspeccionado en vivo: ambos
  organizadores, IDs, montos y números correctos.
- ✅ **AC-03** (FR-04): 1 PDF por cartón, 10 números + GUID. `QuestPdfCartonRenderer.cs`. Reproducido
  en vivo: 2 adjuntos con nombre de archivo = CartonId.
- ✅ **AC-04/AC-05** (FR-05/FR-06): reintento hasta 3 intentos, 1 min entre cada uno, `Fallido` al
  agotar. `EnvioMail.cs:54-65`, `EnvioMailService.cs:75-87`, `EnvioMailRepository.cs:30-36`. Máquina
  de estados cubierta exhaustivamente por tests unitarios + integración real.
- ✅ **AC-06** (FR-07): HTTP 200 sin esperar el mail, `EncolarAsync` nunca bloquea ni propaga.
  `CompraService.cs:116-126`. Reproducido en vivo: respuesta <1s, background tick ~24s después.
- ✅ **AC-07** (FR-08): sobrevive a reinicio — `EnvioMailRepositoryTests.cs:86-99` reconstruye
  `AppDbContext`/repositorio nuevos contra el mismo SQL Server real (simulación genuina de reinicio).

## Spec — tareas por bloque

- ✅ Block 1 (Domain) — 5/5 tests requeridos, presentes y en verde.
- ✅ Block 2 (Application) — 9/9 tests requeridos, incluye el caso de batch mixto (R-04).
- ✅ Block 3 (Infrastructure) — 8/8 tests requeridos + 1 extra (caso `null` defensivo).
- ✅ Migración: backfill `NEWID()` por fila verificado (R-06).
- ✅ Revisión dirigida de logging (R-01/R-02): confirmada línea por línea, ningún log interpola
  `ex.Message` ni PII.

## Coverage

Todas las clases nuevas ≥80% líneas (Domain/Application 100%, Infrastructure 90-100% líneas con
alguna rama defensiva de baja probabilidad sin cubrir, documentada y aceptada).

## Sad paths

Los 5 escenarios negativos del spec (falla SMTP, dato ausente, batch mixto, `EncolarAsync` lanzando,
tick del `BackgroundService` lanzando) tienen test dedicado y pasan.

## Calidad

- ✅ `dotnet build` — 0 warnings, 0 errors.
- ✅ `dotnet format --verify-no-changes` — limpio.
- ✅ Sin dead code ni TODO/FIXME en el diff de este ticket.
- ⚠️ **No bloqueante**: `MailKitEmailSenderTests.DisposeAsync` no verifica el resultado del `DELETE`
  contra smtp4dev — un fallo de cleanup pasaría en silencio (se detectaron 2 mensajes huérfanos de
  una corrida anterior a esta sesión en el inbox de smtp4dev). No afecta la corrección de los tests
  actuales (asunto único por corrida). Pendiente menor para un ticket futuro o el próximo touch de
  este archivo — no amerita un corrective loop por sí solo.

## Reproducción end-to-end

Suite completa reproducida de forma independiente: **259/259** (Domain 49, Application 56,
Infrastructure 72, Api 82). Escenario "Final verification" del spec reproducido REAL de punta a
punta contra la API viva (2 organizadores, 1 comprador, compra confirmada, tick real del
`BackgroundService`, mail confirmado vía la REST API de smtp4dev con 2 adjuntos PDF). Datos de
prueba limpiados tras la verificación.

---

**FAILs: 0 | WARNs: 2 (no bloqueantes) | PASSes: 24**
**Veredicto: PASSED**
