# Parent PRD: Frontend completo (bingos, descubrimiento, carrito, compra, mis cartones y cuenta)

| Metric | Value |
|--------|-------|
| Ticket | FEAT-010 |
| Date | 2026-08-25 |
| Status | Split |

## Sub-tickets

| Sub-ticket | Title | PRD | Dependencies | Status |
|---|---|---|---|---|
| FEAT-010a | Infraestructura transversal y resolución de rol (whoami) | prd-FEAT-010a.md | none | active |
| FEAT-010b | Organizador: gestión de bingos y conciliación de ventas | prd-FEAT-010b.md | depends on a | pending |
| FEAT-010c | Comprador: descubrimiento y carrito | prd-FEAT-010c.md | depends on a | pending |
| FEAT-010d | Comprador: checkout (registro/login y confirmación de compra) | prd-FEAT-010d.md | depends on a, c | pending |
| FEAT-010e | Comprador: mis cartones y datos de cuenta | prd-FEAT-010e.md | depends on a, d | pending |

## Suggested implementation order
a → b, a → c → d → e (b es independiente de c/d/e una vez que a existe)

## Original context

El backend de BingoCart cubre hoy todo el recorrido funcional del organizador y del comprador, pero
el frontend Angular solo implementa registro y login de organizador
(`frontend/src/app/features/auth/`). El PRD original (este archivo, antes de partirse) cubría en un
solo ticket las 9 pantallas/flujos restantes — 25 FR, 5 NFR, 29 AC —, muy por encima del umbral de
`.daw/rules/validation-rules.instructions.md` / `.daw/rules/define.instructions.md` (Scope Control).

El impact scan de PLAN (2026-08-25) confirmó, mirando acoplamiento real de código y no solo áreas de
negocio, que hay 5 grupos independientemente entregables: **(a)** infraestructura transversal —
interceptor HTTP, guards de rol, y el endpoint `GET /api/auth/whoami` (nuevo, mínimo, de backend) que
resuelve el rol de sesión sin exponer el JWT — es prerrequisito duro de todo lo demás; **(b)**
organizador (bingos + conciliación de ventas), aislable una vez que el guard de (a) existe; **(c)**
descubrimiento + carrito, que no dependen de autenticación de comprador; **(d)** checkout (registro/
login de comprador + confirmar compra), que depende del carrito armado en (c); **(e)** mis cartones y
datos de cuenta del comprador, que depende de la sesión de comprador resuelta en (d).

Mismo patrón que FEAT-001a/b y FEAT-009a/b/c/d: cada sub-ticket es un bounded context real, no una
partición de conveniencia.

Referencia: PRD original `docs/daw/prd/prd-FEAT-010.md` (historial de git, commit `6873bf2`), PRD
maestro `docs/daw/prd/prd-bingocartV2.md`.
