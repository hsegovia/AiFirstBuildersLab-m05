# PRD FEAT-001a: Registro de organizador

| Field | Value |
|-------|-------|
| Ticket | FEAT-001a |
| Tracker | none |
| Date | 2026-08-15 |
| PRD loops | 0 |

## Context and Problem

Los organizadores de bingos (clubes, asociaciones, entidades benéficas) no tienen forma de crear una
cuenta en la plataforma. Sin una cuenta, ningún otro requerimiento del organizador (RF-02 y
siguientes del PRD maestro `docs/daw/prd/prd-bingocartV2.md`: crear bingos, ver dashboard, confirmar
pagos) es alcanzable.

Este ticket es el sub-ticket `a` del split de FEAT-001 (ver `docs/daw/prd/prd-FEAT-001.md`, índice)
e implementa exclusivamente la creación de la cuenta de organizador, incluidas sus validaciones. El
login se implementa por separado en FEAT-001b, que depende de este ticket.

Referencia: RF-01, AC-20 (parcial) de `docs/daw/prd/prd-bingocartV2.md`.

## Goals

| # | Objetivo | Métrica de éxito |
|---|----------|-------------------|
| G1 | Permitir a un visitante crear una cuenta de organizador sin fricción innecesaria | Registro completo en un único formulario, sin pasos de verificación adicionales |
| G2 | Impedir el registro con datos de identificación inválidos o duplicados | 0% de cuentas creadas con CUIT mal formado o mail duplicado |
| G3 | Garantizar que la contraseña nunca se almacene en texto plano | 0 contraseñas en texto plano en base de datos |

## Functional Requirements

- FR-01: El sistema debe permitir a un visitante registrarse como organizador indicando nombre de la
  organización, CUIT, mail, teléfono y contraseña.
- FR-02: El sistema debe validar que el CUIT tenga el formato de 11 dígitos numéricos y dígito
  verificador válido según el algoritmo estándar CUIT/CUIL argentino, sin validarlo contra AFIP ni
  ningún padrón externo.
- FR-03: El sistema debe rechazar el registro si el mail ya pertenece a una cuenta de organizador
  existente, indicando en la respuesta que el mail ya está en uso.
- FR-04: El sistema debe exigir que la contraseña cumpla la política por defecto de ASP.NET Core
  Identity: mínimo 8 caracteres, al menos 1 mayúscula, 1 minúscula, 1 dígito y 1 carácter no
  alfanumérico.
- FR-05: El sistema debe validar que el teléfono tenga formato numérico (dígitos, `+`, espacios y
  guiones permitidos) con una longitud de entre 8 y 20 caracteres.
- FR-06: El sistema debe activar la cuenta del organizador inmediatamente al completar el registro,
  sin requerir verificación de mail.
- FR-07: El sistema debe almacenar la contraseña del organizador exclusivamente como hash generado
  por ASP.NET Core Identity, nunca en texto plano.

## Non-Functional Requirements

- NFR-01: Las contraseñas deben almacenarse con el hashing por defecto de ASP.NET Core Identity
  (PBKDF2 con salting), 0 contraseñas almacenadas en texto plano o con algoritmo de hash débil
  (MD5/SHA1), verificable por revisión de código.
- NFR-02: Los datos personales del organizador (CUIT, mail, teléfono) deben almacenarse en SQL
  Server con acceso restringido por rol y sin exposición en logs de aplicación, conforme a RNF-04 y
  RNF-09 de `docs/daw/prd/prd-bingocartV2.md`: 0 apariciones de CUIT, mail o teléfono en logs,
  verificable por revisión de código.
- NFR-03: El endpoint de registro debe responder en menos de 3 segundos p95, incluyendo la
  validación de CUIT y la verificación de unicidad de mail.

## Acceptance Criteria

- AC-01 (FR-01, FR-06): WHEN un visitante envía el formulario de registro con nombre de
  organización, CUIT válido, mail no utilizado, teléfono válido y contraseña que cumple la política,
  THE sistema SHALL crear la cuenta de organizador y dejarla activa.
- AC-02 (FR-02): IF el CUIT ingresado no tiene 11 dígitos numéricos o su dígito verificador no es
  válido, THEN THE sistema SHALL rechazar el registro e informar que el CUIT tiene un formato
  inválido.
- AC-03 (FR-03): IF el mail ingresado ya pertenece a una cuenta de organizador existente, THEN THE
  sistema SHALL rechazar el registro e informar que el mail ya está en uso.
- AC-04 (FR-04): IF la contraseña ingresada no cumple la política (mínimo 8 caracteres, 1
  mayúscula, 1 minúscula, 1 dígito, 1 carácter no alfanumérico), THEN THE sistema SHALL rechazar el
  registro e informar los requisitos de contraseña no cumplidos.
- AC-05 (FR-05): IF el teléfono ingresado no es numérico (permitiendo `+`, espacios y guiones) o su
  longitud no está entre 8 y 20 caracteres, THEN THE sistema SHALL rechazar el registro e informar
  que el teléfono tiene un formato inválido.
- AC-06 (FR-07): WHEN se consulta la base de datos tras un registro exitoso, THE sistema SHALL
  mostrar la contraseña almacenada como un hash generado por ASP.NET Core Identity, nunca en texto
  plano.

## Out of Scope

- Login / autenticación de la cuenta creada — se implementa en FEAT-001b.
- Emisión de JWT y manejo de sesión — se implementa en FEAT-001b.
- Recuperación de contraseña ("olvidé mi contraseña") — ticket posterior.
- Edición de perfil del organizador (cambiar nombre de organización, mail, teléfono ya registrados).
- Verificación de mail (envío de link de confirmación) — la cuenta queda activa sin este paso.
- Roles múltiples o permisos granulares dentro de la cuenta de organizador.
- Autenticación con redes sociales, SSO o 2FA (fuera de alcance del producto completo).
- Creación de bingos y cualquier funcionalidad posterior al login (RF-02 en adelante del PRD
  maestro).
- Validación del CUIT contra AFIP o cualquier padrón externo (ver R-05 del PRD maestro).

## Risks and Mitigations

| # | Riesgo | Impacto | Mitigación |
|---|--------|---------|------------|
| R-01 | **Enumeración de cuentas por mensaje de mail duplicado**: informar explícitamente "mail ya en uso" en el registro permite a un atacante enumerar qué mails están registrados como organizadores. | Bajo — el organizador no es un dato tan sensible como un comprador final, y esta respuesta explícita fue una decisión deliberada del usuario del producto para priorizar UX sobre esta mitigación específica. | Aceptado como riesgo por decisión explícita del usuario. Si se detecta abuso (scraping de mails registrados), reevaluar hacia un mensaje genérico. |

## Dependencies

Ninguna. Este es el primer sub-ticket del proyecto: no depende de código ni de otro ticket existente.
Es, en cambio, una dependencia de FEAT-001b (login), que requiere una cuenta de organizador ya
creada por este ticket.
