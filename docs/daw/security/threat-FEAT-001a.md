# Threat Model FEAT-001a: Registro de organizador

| Field | Value |
|-------|-------|
| Ticket | FEAT-001a |
| Spec | docs/daw/specs/spec-FEAT-001a.md |
| Date | 2026-08-15 |

## Componentes analizados

1. `OrganizadoresController` (`POST /api/organizadores/registro`) — Bloque 4, endpoint público
2. `OrganizadorService` / `IIdentityGateway` (Application/Infrastructure) — Bloque 3
3. `Organizador`, `CuitValidator`, `TelefonoValidator` (Domain) — Bloque 2
4. `IdentityGateway` → `UserManager<ApplicationUser>` → SQL Server (`AspNetUsers`) — Bloque 1/3
5. `RegistroOrganizadorComponent` (Angular SPA) — Bloque 6
6. Red de `docker-compose` (`db`, `api`, `web`) — Bloque 7

## Trust boundaries (F-TM-02)

| Boundary | Entre | Protocolo |
|---|---|---|
| TB-1 | Navegador (no confiable, público) ↔ `OrganizadoresController` (Api) | HTTP/HTTPS |
| TB-2 | `IdentityGateway` (Infrastructure) ↔ SQL Server (`db`) | TDS / connection string |
| TB-3 | Host ↔ contenedores `docker-compose` (puertos 8080, 8000, 14330 expuestos) | TCP/red Docker |

## Clasificación de datos sensibles (F-TM-05)

| Dato | Clasificación | Dónde vive |
|---|---|---|
| `Password` | Credencial | Nunca persiste como tal — solo su hash (`PasswordHash`, Identity) |
| `Cuit`, `Mail`, `Telefono`, `NombreOrganizacion` | PII (Ley 25.326, RNF-09 del PRD maestro) | `AspNetUsers` (SQL Server) |

## STRIDE por componente

### `OrganizadoresController` (TB-1)

| STRIDE | Análisis |
|---|---|
| Spoofing | Sin autenticación por diseño (registro público, RF-01). No hay verificación de que quien registra realmente representa la organización — riesgo ya aceptado a nivel de producto (R-05 del PRD maestro: CUIT no se valida contra AFIP). No es un riesgo nuevo de este ticket. |
| Tampering | Body HTTP interceptable si el tráfico no va sobre HTTPS. Mitigación: HTTPS obligatorio en todo el tráfico de la plataforma (asunción de infraestructura ya declarada en `docs/daw/prd/prd-FEAT-001a.md` R-02/R-03 equivalentes de FEAT-001b, aplica igual aquí). |
| Repudiation | Sin log de auditoría del evento de registro exitoso. → **Mitigación folded**: log INFO tras éxito con solo el `Guid` generado, nunca CUIT/mail/teléfono (Bloque 4). |
| Information Disclosure | (a) Errores no controlados podrían filtrar stack traces → mitigado por `ExceptionHandlingMiddleware` (Bloque 4, ya en el spec). (b) Respuesta 409 `MailYaRegistrado` permite enumerar mails registrados → ver **Riesgo Aceptado #1** abajo. |
| Denial of Service | Endpoint público sin autenticación ni límite de solicitudes — expuesto a spam/DoS de bajo esfuerzo. → **Mitigación folded**: rate limiting fijo, 5 req/min por IP (Bloque 4). |
| Elevation of Privilege | No aplica — la cuenta creada es rol "Organizador" sin escalamiento posible desde este endpoint. |

### `OrganizadorService` / `IIdentityGateway` / Domain (`Organizador`, validadores)

| STRIDE | Análisis |
|---|---|
| Spoofing | N/A — capas internas, no expuestas directamente. |
| Tampering | Los validadores (`CuitValidator`, `TelefonoValidator`) son funciones puras sin I/O — no hay superficie de tampering adicional. |
| Repudiation | Cubierto por el log de auditoría del Bloque 4. |
| Information Disclosure | Las excepciones de dominio (`CuitInvalidoException`, etc.) no incluyen el valor completo inválido en el mensaje — ya especificado en el spec (Bloque 2). |
| Denial of Service | N/A. |
| Elevation of Privilege | N/A. |

### `IdentityGateway` → SQL Server (TB-2)

| STRIDE | Análisis |
|---|---|
| Spoofing | Conexión autenticada por connection string. → **Mitigación folded**: nunca hardcodeada, inyectada por variable de entorno (Bloque 1). |
| Tampering | Tráfico API↔DB sin cifrar por defecto. → **Mitigación folded**: `Encrypt=True` en la connection string (Bloque 1). |
| Repudiation | N/A (fuera de alcance — no se audita a nivel de DB en este ticket). |
| Information Disclosure | PII y hash de password persistidos sin cifrado at-rest explícito. → **Mitigación folded**: habilitar TDE en la instancia de SQL Server (Bloque 1, F-TM-07). |
| Denial of Service | N/A — un solo servicio consumidor (la API), sin carga externa directa a la DB. |
| Elevation of Privilege | N/A — el usuario de conexión de la API debe tener permisos acotados a su propia base (buena práctica general, no requiere cambio de diseño en este ticket). |

### `RegistroOrganizadorComponent` (SPA)

| STRIDE | Análisis |
|---|---|
| Spoofing | N/A — no autentica, solo envía datos. |
| Tampering | Validación cliente es solo UX; la autoritativa es el backend (ya documentado en el spec, Bloque 6). |
| Repudiation | N/A. |
| Information Disclosure | El formulario no debe loguear el password en la consola del navegador ni en herramientas de analytics — nota de implementación, sin cambio de arquitectura. |
| Denial of Service | N/A (mitigado del lado servidor por el rate limiting). |
| Elevation of Privilege | N/A. |

### Red `docker-compose` (TB-3)

| STRIDE | Análisis |
|---|---|
| Spoofing/Tampering | Puerto de `db` (14330) expuesto al host. → Ver **Riesgo Aceptado #2** abajo. |
| Information Disclosure | Imágenes oficiales de Microsoft/node/nginx, sin dependencias de terceros nuevas fuera de lo declarado en `AGENTS.md` (W-TM-01: no aplica, sin dependencias nuevas). |
| Denial of Service | N/A — entorno de desarrollo local, no expuesto a internet. |
| Elevation of Privilege | N/A. |

## Riesgos identificados y disposición

| # | Riesgo | STRIDE | Likelihood | Impact | Disposición |
|---|---|---|---|---|---|
| 1 | Enumeración de cuentas vía 409 `MailYaRegistrado` | Information Disclosure | Medium | Low | **Riesgo aceptado** (ver abajo) |
| 2 | Puerto de `db` expuesto al host en `docker-compose` | Tampering/Info Disclosure | Low (solo local) | Medium (si se reutiliza el compose fuera de dev) | **Riesgo aceptado** (ver abajo) + nota operativa en Bloque 7 |
| 3 | Spam/DoS por falta de rate limiting en endpoint público | Denial of Service | Medium | Medium | **Mitigado** — rate limiting 5 req/min/IP (Bloque 4) |
| 4 | PII y hash de password sin cifrado explícito en tránsito/reposo hacia SQL Server | Information Disclosure | Low | High (dato personal, Ley 25.326) | **Mitigado** — `Encrypt=True` + TDE (Bloque 1). Nota (2026-08-16, corrective loop VERIFY→CODE): TDE estaba declarado acá pero nunca implementado — solo un comentario en `docker-compose.yml`. Se verificó real (no solo declarado): `AppDbContextTdeExtensions.EnsureTdeEnabledAsync` corre en cada arranque de la Api y se confirmó `is_encrypted = 1` sobre la base `BingoCart` con una consulta directa a `sys.databases`, además del test de integración `AppDbContextTdeExtensionsTests`. |
| 5 | Sin log de auditoría del registro exitoso | Repudiation | Low | Low | **Mitigado** — log INFO sin PII (Bloque 4) |
| 6 | Connection string hardcodeada | Spoofing | Low | High (si ocurriera) | **Mitigado** — solo por variable de entorno (Bloque 1) |

Ningún riesgo alcanza Critical o High: la mitigación es viable y de bajo costo para todos, salvo los
dos formalmente aceptados abajo (que son, en sí mismos, de impacto Low/Medium).

## Riesgos aceptados (F-TM-04)

### Riesgo Aceptado #1 — Enumeración de cuentas por mail duplicado

| Campo | Valor |
|---|---|
| Quién lo aceptó | El usuario (product owner de este proyecto), durante la fase DEFINE de FEAT-001a |
| Justificación | Decisión explícita: priorizar UX (mensaje claro de "mail ya en uso") sobre la mitigación de enumeración, dado que el organizador no es un dato tan sensible como un comprador final. Documentado como R-01 en `docs/daw/prd/prd-FEAT-001a.md`. |
| Condiciones de revisión | Reevaluar si se detecta abuso real (scraping de mails registrados) reportado por un organizador o detectado en logs de tráfico anómalo. Sin fecha de revisión fija — revisión disparada por evidencia de abuso, no por calendario. |

### Riesgo Aceptado #2 — Puerto de SQL Server expuesto al host

| Campo | Valor |
|---|---|
| Quién lo aceptó | El usuario (product owner de este proyecto), durante la fase PLAN de FEAT-001a |
| Justificación | El `docker-compose.yml` de este ticket es para desarrollo local; exponer el puerto 14330 al host es necesario para depurar/administrar la base durante el desarrollo. No hay despliegue productivo en el alcance de este ticket. |
| Condiciones de revisión | Antes de usar este mismo `docker-compose.yml` (o una variante) en cualquier entorno compartido o accesible fuera de la máquina del desarrollador, se debe remover la publicación del puerto de `db` — ya documentado como nota operativa en el Bloque 7 del spec. |

## Mitigaciones plegadas al spec

1. Rate limiting (5 req/min/IP) sobre `POST /api/organizadores/registro` — Bloque 4.
2. Log de auditoría del registro exitoso, sin PII — Bloque 4.
3. `Encrypt=True` en la connection string hacia SQL Server — Bloque 1.
4. TDE habilitado en la instancia de SQL Server — Bloque 1.
5. Connection string nunca hardcodeada, solo por variable de entorno — Bloque 1.
6. Nota operativa: puerto de `db` no debe exponerse fuera de la red interna de `docker-compose` en
   ningún entorno más allá de desarrollo local — Bloque 7.

## Resultado

Riesgos: C:0 H:0 M:2 L:4 (2 formalmente aceptados con los 3 campos de F-TM-04, 4 mitigados en el
spec). Todas las reglas F-TM-01 a F-TM-07 satisfechas.

**PASSED** — sin riesgos Critical/High pendientes de mitigación.
