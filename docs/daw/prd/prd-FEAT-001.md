# Parent PRD: Registro y autenticación de organizador

| Metric | Value |
|--------|-------|
| Ticket | FEAT-001 |
| Date | 2026-08-15 |
| Status | Split |

## Sub-tickets

| Sub-ticket | Title | PRD | Dependencies | Status |
|---|---|---|---|---|
| FEAT-001a | Registro de organizador | prd-FEAT-001a.md | none | done — PR #1 (draft), se mergea cuando se apruebe |
| FEAT-001b | Login de organizador | prd-FEAT-001b.md | depends on a | active |

## Suggested implementation order
a → b

## Original context

Los organizadores de bingos no tienen forma de acceder a la plataforma: no existe cuenta, no existe
login. Sin esto, ningún otro requerimiento del organizador (RF-02 y siguientes del PRD maestro
`docs/daw/prd/prd-bingocartV2.md`: crear bingos, ver dashboard, confirmar pagos) es alcanzable.

El PRD original cubría en un solo ticket el registro de cuenta (crear organizador, validar CUIT,
mail, teléfono, contraseña) y la autenticación (login, JWT, lockout). Tenía 10 criterios de
aceptación, por encima del umbral de 5-7 de `.daw/rules/validation-rules.instructions.md` /
`.daw/rules/define.instructions.md` (Scope Control), así que se dividió en dos sub-tickets
independientemente entregables: **a) registro** y **b) login**, donde b depende de a (no se puede
loguear una cuenta que no existe).

Referencia: RF-01, RF-01b, AC-20 de `docs/daw/prd/prd-bingocartV2.md`.
