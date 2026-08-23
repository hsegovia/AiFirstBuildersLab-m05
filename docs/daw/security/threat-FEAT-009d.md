# Threat Model — FEAT-009d ("Mis cartones" del comprador y actualización de datos de cuenta)

| Field | Value |
|-------|-------|
| Ticket | FEAT-009d |
| Date | 2026-08-23 |
| Spec | docs/daw/specs/spec-FEAT-009d.md (PASSED, 6 bloques) |
| PRD | docs/daw/prd/prd-FEAT-009d.md (12 FR, 5 NFR, 15 AC, loop 2) |
| Veredicto | **PASSED** — todo CRITICAL/HIGH con mitigación plegada al spec |
| Rondas | Ronda 1: MITIGATION REQUIRED (R-01 HIGH sin decidir). Ronda 2: PASSED tras el corrective loop a DEFINE que agregó FR-13/AC-16 |

## Contexto de autenticación verificado (no asumido)

Base de todo el análisis, confirmada contra el código:

- El esquema por defecto es **JWT Bearer stateless** (`Program.cs:157-163`). `AddIdentity`
  (`Program.cs:42`) registra el esquema de cookie de Identity, pero **no es el que autentica las
  requests**: un evento `OnMessageReceived` (`Program.cs:190`) extrae el token de la cookie
  `bingocart_auth` y lo valida como Bearer.
- **No hay `SecurityStampValidator` en el pipeline.** Ese validador pertenece a la autenticación por
  cookie de Identity, que acá no se usa para autenticar.
- `TokenValidationParameters` valida issuer, audience, firma y lifetime (`Program.cs:175-183`).
- Vida del token: **60 minutos** (`appsettings.json:18`).
- Cookie: `HttpOnly = true`, `Secure = true`, `SameSite = Strict`, `Path = "/"`
  (`CompradoresController.cs:65-72`).

**Consecuencia que atraviesa todo este documento:** rotar el `SecurityStamp` **no invalida ningún
token ya emitido**. No hay revocación de sesión en este sistema; un token vive hasta que expira.

## Clasificación de datos sensibles (F-TM-05)

| Dato | Clasificación | Dónde aparece en este ticket |
|---|---|---|
| Mail del comprador | **PII + credencial de acceso** (es el identificador de login) | `PUT /api/compradores/mi-cuenta` (entrada y salida), claim `Email` del JWT, `AspNetUsers.Email`/`UserName` |
| Nombre y apellido | PII | `PUT /api/compradores/mi-cuenta` (entrada y salida), `AspNetUsers` |
| CUIT | **PII de identificación fiscal** | `PUT /api/compradores/mi-cuenta` (entrada y salida), `AspNetUsers.Cuit` (índice UNIQUE) |
| Cartones adquiridos (id, correlativo, números, bingo, organización, estado de pago) | Datos de negocio vinculados a una persona identificada | `GET /api/compradores/mis-cartones`, PDF |
| Número correlativo del cartón | **Público** (por decisión D-07) | 5 proyecciones, dos de ellas anónimas |
| Contraseña | Credencial | **Fuera de alcance**: este ticket no la lee, no la escribe y no la exige |

**Cifrado (F-TM-07).** En tránsito: TLS obligatorio; la cookie es `Secure`, de modo que el navegador
no la envía por HTTP. En reposo: la PII vive en `AspNetUsers` bajo la TDE ya habilitada en el motor
(decisión de FEAT-001a, ADR-001) — este ticket no agrega ningún almacén nuevo. El PDF **no se
persiste**: se genera en memoria por request y se devuelve; no hay archivo en disco, ni caché, ni
blob storage que proteger. Esa es una propiedad deseable y el spec debe conservarla.

## Fronteras de confianza (F-TM-02)

| # | Frontera | Qué la cruza | Control |
|---|---|---|---|
| TB-1 | Internet anónimo → API pública | Descubrimiento y carrito, `[AllowAnonymous]` (`CartonesController.cs:16`, `CarritoController.cs:20`) — ahora incluyen el correlativo | Ninguno por diseño; solo datos clasificados como públicos |
| TB-2 | Internet anónimo → API autenticada | Los 3 endpoints del ticket | JWT Bearer validado + `[Authorize(Roles = "Comprador")]` + rate limiting `"comprador-cuenta"` |
| TB-3 | Comprador autenticado A → datos del comprador B | `GET mis-cartones`, `GET .../pdf` | `compradorId` derivado **solo** del claim `NameIdentifier`; 404 indistinguible |
| TB-4 | Api → Application | Los 3 flujos | DTO tipados; ningún `compradorId` viaja por ruta, query ni cuerpo |
| TB-5 | Application → Identity (`UserManager`) | `PUT mi-cuenta` | Puerto `ICompradorIdentityGateway`; Application nunca ve `UserManager` |
| TB-6 | Aplicación → SQL Server | Consultas del listado y de sorteo inminente; migración | LINQ parametrizado por EF Core; la migración usa SQL literal sin input |
| TB-7 | Migración → datos productivos existentes | Backfill del correlativo sobre `Cartones` | Transacción de migración de EF Core; `AlterColumn` NOT NULL solo tras el backfill |

## STRIDE por componente

### Domain — `Carton`, `NumeroCorrelativoInvalidoException`, `CartonNoEncontradoException`, `PlazoModificacionVencidoException`, `MailEnUsoException`, `CuitEnUsoException`

| STRIDE | Evaluación |
|---|---|
| **S** | N/A — el dominio no autentica. |
| **T** | `Carton` sigue siendo inmutable (`private init`); el correlativo se fija en el factory y no se puede alterar después. El factory valida `>= 1`. |
| **R** | N/A — sin logging en Domain, por convención del proyecto. |
| **I** | **Hallazgo:** los mensajes de `MailEnUsoException` y `CuitEnUsoException` viajan al cliente (el middleware devuelve el mensaje de dominio). Si echan el valor recibido, el error confirma que *ese* mail o *ese* CUIT tiene cuenta. Ver R-04. |
| **D** | N/A. |
| **E** | N/A — sin lógica de autorización en Domain. |

### Application — `MisCartonesService`, `CompradorService`, `ICompradorIdentityGateway`

| STRIDE | Evaluación |
|---|---|
| **S** | El `compradorId` llega como parámetro desde el controller, que lo saca del claim. Ningún método lo acepta desde un DTO de entrada. ✅ |
| **T** | La actualización es total y atómica: todas las validaciones ocurren **antes** de la primera escritura, así que un rechazo no deja la cuenta a medias (AC-08, AC-11). |
| **R** | El flujo de actualización de datos **no deja rastro de quién cambió qué ni cuándo**. Ver R-05. |
| **I** | 4 campos de PII simultáneos en el flujo de actualización — la mayor superficie de PII del proyecto hasta ahora. El spec prohíbe PII y `ex.Message` en logs; SAST debe verificarlo (F-SAST-10). |
| **D** | El clamp de `pageSize` a 50 acota el costo del listado. |
| **E** | `ICompradorIdentityGateway` es el puerto del comprador; los métodos nuevos no se agregan al de organizador, así que no se abre un camino para que un comprador toque datos de organizador. ✅ |

### Infrastructure — `CompraRepository`, `CompradorCuentaRepository`, `IdentityGateway`, `QuestPdfCartonRenderer`, migración

| STRIDE | Evaluación |
|---|---|
| **S** | `IdentityGateway` actualiza `UserName`/`NormalizedUserName` junto con `Email`: sin eso, el índice único sobre `NormalizedUserName` quedaría envenenado y un registro posterior con el mail viejo fallaría de forma inexplicable. ✅ |
| **T** | La migración usa SQL literal (`ROW_NUMBER() OVER (PARTITION BY BingoId ORDER BY Id)`) **sin ningún input de usuario concatenado ni interpolado**: no hay superficie de inyección. El backfill corre dentro de la transacción de migración; si falla, el `AlterColumn` a NOT NULL no llega a ejecutarse y la tabla no queda a medias. ✅ |
| **R** | Sin cambios en la política de auditoría existente. |
| **I** | Las consultas del listado proyectan campos explícitos y nunca materializan `ApplicationUser`, siguiendo los 3 precedentes de join a `Users` — no hay riesgo de arrastrar `PasswordHash` a un DTO. ✅ |
| **D** | `QuestPdfCartonRenderer` es trabajo de CPU sincrónico por request. Ver R-03. |
| **E** | N/A. |

### Api — 3 endpoints, `ExceptionHandlingMiddleware`, política `"comprador-cuenta"`

| STRIDE | Evaluación |
|---|---|
| **S** | **Riesgo principal del ticket.** Un token robado permite cambiar el mail sin conocer la contraseña. Ver R-01. |
| **T** | `SameSite = Strict` + JSON + Bearer bloquean el CSRF clásico sobre el `PUT`. ✅ |
| **R** | Ver R-05. |
| **I** | 404 indistinguible entre cartón ajeno e inexistente. El 200 del PDF sale `application/pdf` y el 404 sale JSON; ambos content-types conviven a propósito y ninguno filtra internos. |
| **D** | Rate limiting nuevo de 30 req/5 min por comprador en los 3 endpoints. Ver R-03. |
| **E** | `[Authorize(Roles = "Comprador")]` en los 3. Un organizador autenticado no alcanza ninguno. ✅ |

---

## Riesgos

### 🟠 R-01 — HIGH — Apoderamiento de cuenta por cambio de mail sin re-autenticación ni verificación

| Campo | Valor |
|---|---|
| STRIDE | Spoofing / Elevation of Privilege |
| Probabilidad | Baja |
| Impacto | Alto |

**El escenario.** El `PUT /api/compradores/mi-cuenta` cambia el mail —que en este sistema **es la
credencial de login**, porque `AutenticarAsync` resuelve al usuario con `FindByEmailAsync`— sin pedir
la contraseña actual y sin verificar que el comprador controle la dirección nueva. Quien consiga un
token válido puede, en un solo request:

1. Cambiar el mail a uno propio. La víctima **deja de poder iniciar sesión**: su dirección ya no
   resuelve a ninguna cuenta, y el proyecto no tiene recuperación de contraseña.
2. Redirigir los mails de confirmación pendientes. D-04 ya dejó documentado que `EnvioMail` guarda
   `CompradorId` y resuelve el destinatario **en tiempo de envío**, así que las confirmaciones de
   compras ya hechas —con sus cartones en PDF adjuntos— salen a la dirección nueva.

**Lo que sí protege hoy.** La cookie es `HttpOnly` (bloquea el robo por XSS), `Secure` (no viaja en
claro) y `SameSite = Strict` (bloquea el CSRF). El token dura 60 minutos. La vía realista es acceso
físico a un dispositivo desbloqueado o una sesión compartida, no un ataque remoto.

**Lo que no protege.** Nada exige demostrar posesión de la contraseña ni del mail nuevo, y **la
rotación del `SecurityStamp` no revoca las sesiones existentes** (ver R-02), así que no hay ningún
control compensatorio del lado de la sesión.

**Mitigaciones posibles — requieren decisión del usuario:**

- **(a) Exigir la contraseña actual en el `PUT`.** Es el control estándar para cambiar una
  credencial. Cuesta un campo más en `ActualizarCuentaRequest` y una llamada de verificación en
  `IdentityGateway`. **Está fuera del alcance del PRD**: A-03 fija que la solicitud lleva los cuatro
  campos, y RF-21 enumera apellido, nombre, CUIT y mail. Agregarlo exige un corrective loop a DEFINE.
- **(b) Notificar a la dirección **anterior** que el mail cambió.** Da a la víctima una chance de
  reaccionar. Requiere un tipo de envío nuevo en el outbox de FEAT-009b; también fuera del PRD.
- **(c) Aceptar el riesgo formalmente** (F-TM-04: quién lo acepta, justificación, condiciones de
  revisión) y abrirlo como ticket propio.

**No propongo una por defecto**: (a) y (b) son cambios de alcance que el usuario tiene que aprobar, y
(c) es una decisión suya, no mía.

### ✅ R-01 — RESUELTO (ronda 2)

Decisión del usuario (hsegovia, 2026-08-23): **opción (a), exigir la contraseña actual**. Se aplicó
el corrective loop PLAN→DEFINE, el PRD sumó **FR-13 y AC-16** (loop 3, validación PASSED) y el spec
plegó la mitigación en Block 5 y Block 6:

- La solicitud lleva `ContrasenaActual`, que se verifica contra Identity a través del puerto del
  comprador —reutilizando el mecanismo de `AutenticarAsync`, sin comparar `PasswordHash` a mano—.
- **La verificación es la primera de todas**, antes de validar el CUIT y antes de consultar las
  colisiones. Ese orden también degrada R-04: sin la contraseña correcta, las respuestas "ese mail ya
  está en uso" / "ese CUIT ya está en uso" son inalcanzables, así que el oráculo de enumeración deja
  de estar disponible para quien solo tenga una sesión robada.
- Rechazo con 403 `"ContrasenaIncorrecta"` (no 401: la sesión es válida, lo que falta es la prueba de
  identidad para esta operación concreta), mensaje genérico y ningún dato modificado.
- La contraseña nunca se persiste, nunca se devuelve y nunca se escribe en un log.

**Riesgo residual, documentado como R-10 en el PRD:** no se verifica que el comprador controle la
dirección **nueva**. Un error de tipeo deja la cuenta con un mail al que no llega nada. Resolverlo
requiere un flujo de verificación de mail que este proyecto no tiene en ningún lado; queda como
ticket propio, fuera del alcance de FEAT-009d.

### 🟡 R-02 — MEDIUM — La reemisión de cookie no invalida las sesiones existentes

| Campo | Valor |
|---|---|
| STRIDE | Spoofing |
| Probabilidad | Alta (es el comportamiento normal, no un fallo) |
| Impacto | Bajo por sí solo; agrava R-01 |

D-05 reemite la cookie tras una actualización exitosa y el spec rota el `SecurityStamp`. En un
sistema con autenticación por cookie de Identity eso invalidaría las demás sesiones. **Acá no**: el
esquema es JWT Bearer stateless sin `SecurityStampValidator`, así que todo token ya emitido sigue
siendo válido hasta su expiración natural.

El riesgo no es el comportamiento en sí, sino **creer que existe una revocación que no existe**: si
alguien más adelante razona "cambiar el mail cierra las otras sesiones", va a construir sobre una
garantía falsa.

**Mitigación (se pliega al spec, sin cambio de alcance):** el spec debe decir explícitamente que la
reemisión de cookie es **cosmética** —refresca el claim `Email` para que el token no cargue un dato
falso— y que **no revoca nada**. La rotación del `SecurityStamp` se mantiene igual, porque es lo
correcto para el día que se agregue validación de stamp, pero no se le atribuye un efecto que hoy no
tiene. Efecto lateral que conviene notar: la sesión de la propia víctima también sobrevive hasta 60
minutos, lo que le deja una ventana para revertir el cambio.

### 🟡 R-03 — MEDIUM — El endpoint de PDF como vector de agotamiento de CPU

| Campo | Valor |
|---|---|
| STRIDE | Denial of Service |
| Probabilidad | Baja |
| Impacto | Medio |

Es el único endpoint del proyecto que hace trabajo de CPU no trivial por request: `QuestPdfCartonRenderer`
compone y serializa un documento en cada llamada, de forma sincrónica.

**Lo que acota el riesgo, verificado:** el tamaño del PDF es **fijo** —siempre un cartón de 10
números—, así que **no hay amplificación por input**: un atacante no puede pedir un documento más
caro. Cada comprador tiene su propio balde de 30 permits / 5 min (6 por minuto). Y solo se puede
pedir el PDF de cartones **propios**, lo que exige haber comprado.

**El camino de amplificación real** es registrar muchas cuentas, ya que la partición es por
`NameIdentifier`. Eso está acotado aguas arriba: el registro usa la política `"compradores"`, 5/min
particionada por **IP**.

**Mitigación (se pliega al spec):** mantener 30 req/5 min, y dejar escrito en el spec que el
renderer no debe recibir concurrencia ilimitada ni cachear PDF en disco —generar en memoria y
devolver—, para que la superficie siga siendo solo CPU y no también almacenamiento.

### 🟡 R-04 — MEDIUM — Enumeración de cuentas por mail y por CUIT

| Campo | Valor |
|---|---|
| STRIDE | Information Disclosure |
| Probabilidad | Media |
| Impacto | Medio |

AC-08 y AC-11 exigen informar que el mail o el CUIT "ya está en uso". Eso convierte al `PUT` en un
oráculo: un comprador autenticado puede probar direcciones y CUIT de a uno y averiguar cuáles tienen
cuenta. Con el CUIT es peor que con el mail, porque un CUIT identifica unívocamente a una persona
física o jurídica real.

**Es una tensión genuina entre dos requisitos**, no un descuido: un error genérico protegería la
privacidad pero incumpliría los AC, que existen para que el comprador entienda por qué se rechazó su
cambio.

**Mitigación (se pliega al spec, sin cambio de alcance):**

1. Los mensajes de `MailEnUsoException` y `CuitEnUsoException` **no deben repetir el valor enviado**.
   Decir "ese CUIT ya está en uso" cumple el AC; decir "el CUIT 20-XXXXXXXX-3 ya está en uso" lo
   cumple igual y además deja PII en la respuesta y en cualquier log que la capture.
2. El sondeo queda acotado por `"comprador-cuenta"`: 30 intentos cada 5 minutos, particionados por
   comprador y por lo tanto atribuibles a una cuenta identificada.
3. El residuo —que la distinción exista— se documenta como consecuencia aceptada de AC-08/AC-11.

### 🟡 R-05 — MEDIUM — La actualización de datos de cuenta no deja rastro

| Campo | Valor |
|---|---|
| STRIDE | Repudiation |
| Probabilidad | Media |
| Impacto | Medio |

El `PUT` cambia datos con los que el organizador identifica al ganador de un sorteo, y **no queda
registro de cuáles eran antes ni de cuándo cambiaron**. Si un comprador afirma "mi apellido siempre
fue este" tras un sorteo, no hay forma de contradecirlo. Es justamente la clase de disputa que la
ventana de 60 minutos (FR-07) intenta prevenir, atacada desde otro ángulo.

**Mitigación (se pliega al spec, sin cambio de alcance):** registrar un log de auditoría del cambio
con `compradorId`, timestamp UTC y **la lista de nombres de campo que cambiaron** — nunca sus
valores, ni el viejo ni el nuevo, para no violar la disciplina de PII. Eso da trazabilidad de que
hubo un cambio y cuándo, sin escribir PII en los logs. Un historial de valores anteriores sería un
almacén nuevo de PII y queda fuera de alcance.

### 🟢 R-06 — LOW — El correlativo en superficies anónimas

| Campo | Valor |
|---|---|
| STRIDE | Information Disclosure |
| Probabilidad | Alta (es el comportamiento diseñado) |
| Impacto | Bajo |

**Evaluado con criterio propio, no repitiendo el argumento de D-07.** La pregunta es si el
correlativo habilita algo que el GUID no habilitaba. Respuesta: **sí, una cosa, y no es de
seguridad.**

- **No habilita acceso.** Ninguna ruta ni DTO lo acepta como entrada; todas usan constraint `:guid`.
  Adivinar "cartón 37" no da acceso a nada. AC-13 y AC-15 lo verifican con test.
- **No revela existencia.** El descubrimiento ya publica hoy el GUID y los 10 números de cada cartón
  disponible.
- **Sí revela estructura.** El GUID es un espacio disperso y aleatorio; el correlativo es un rango
  **denso y ordenado de 1 a N**. Un observador anónimo que antes solo podía contar los disponibles
  ahora puede inferir el total del bingo y qué posiciones exactas ya se vendieron, y siguiéndolo en
  el tiempo obtiene la curva de ventas de un organizador.

Eso es **inteligencia comercial filtrada a un competidor**, no una vulnerabilidad: no cruza ninguna
frontera de autorización. Se registra como riesgo LOW aceptado por D-07, con la observación de que si
alguna vez importa, la mitigación no es ocultar el número sino no exponer los cartones **no
disponibles** en la superficie anónima.

### 🟢 R-07 — LOW — Evasión de la ventana de 60 minutos

| Campo | Valor |
|---|---|
| STRIDE | Tampering |
| Probabilidad | Media |
| Impacto | Bajo |

La ventana solo bloquea sorteos futuros dentro de los próximos 60 minutos (D-01). Un comprador puede
cambiar sus datos a los 61 minutos del sorteo y el organizador conciliar a los 30 viendo datos
distintos de los que tenía. El control **acota** la ventana de disputa, no la elimina, y eso es
deliberado: el PRD ya lo tiene documentado como R-04 aceptado.

La evaluación en el servidor con `TimeProvider` (NFR-05) elimina la variante de manipular el reloj
del cliente. El margen residual es de segundos entre el envío y el procesamiento.

**Mitigación:** ninguna adicional. R-05 (log de auditoría) cubre parcialmente el caso, porque al
menos deja constancia de que hubo un cambio y cuándo.

### 🟢 R-08 — LOW — Inyección o corrupción en la migración del correlativo

| Campo | Valor |
|---|---|
| STRIDE | Tampering |
| Probabilidad | Muy baja |
| Impacto | Alto si ocurriera |

Primera vez que el proyecto usa SQL crudo en una migración para un valor calculado por fila.
Verificado: la sentencia es un literal constante, sin parámetros, sin concatenación y sin ningún dato
de usuario — **no hay superficie de inyección**. El orden de los tres pasos (columna nullable →
backfill → `AlterColumn` NOT NULL) garantiza que una falla del backfill impide que la columna quede
NOT NULL sin datos. El índice único se crea **después** del backfill: crearlo antes lo haría fallar
contra las filas todavía en NULL.

**Mitigación (ya en el spec):** el test
`MigracionCorrelativoTests.Down_RevierteColumnaEIndiceSinDejarEstadoParcial`, más el criterio de
finalización de Block 1 que exige verificar el resultado del backfill sobre una base con cartones
preexistentes.

---

## Mitigaciones a plegar al spec

Las cinco siguientes **no cambian el alcance** y se pliegan directamente:

1. **R-02** — declarar en Block 6 que la reemisión de cookie es cosmética (refresca el claim `Email`)
   y **no revoca sesiones**, porque el pipeline es JWT Bearer stateless sin `SecurityStampValidator`.
   No atribuirle un efecto de seguridad que no tiene.
2. **R-03** — declarar en Block 4 que el PDF se genera **en memoria y no se persiste** (ni disco, ni
   caché, ni blob), y que el tamaño es fijo, de modo que no hay amplificación por input.
3. **R-04** — los mensajes de `MailEnUsoException` y `CuitEnUsoException` **no repiten el valor
   recibido**. Test asociado en Block 5/6.
4. **R-05** — log de auditoría del cambio de datos con `compradorId`, timestamp UTC y **nombres** de
   los campos modificados, nunca sus valores. Test que verifique que el log no contiene PII.
5. **R-08** — ya cubierta por el test de migración y el criterio de finalización de Block 1.

Y la sexta, resuelta en la ronda 2:

6. **R-01 (HIGH)** — `ContrasenaActual` verificada **antes que cualquier otra validación**, con
   rechazo 403 y sin tocar ningún dato. Respaldada por FR-13/AC-16 del PRD (loop 3) y plegada a
   Block 5 (lógica y orden) y Block 6 (contrato, código de error y tests).

---

## Resumen

```
Superficies de ataque identificadas: 7
Fronteras de confianza declaradas: 7 (TB-1 a TB-7)

🔴 CRITICAL: 0
🟠 HIGH:     1  (R-01 — MITIGADO en ronda 2 vía FR-13/AC-16)
🟡 MEDIUM:   4  (R-02, R-03, R-04, R-05 — las 4 plegadas al spec)
🟢 LOW:      3  (R-06, R-07, R-08 — aceptados y documentados)
```

**Veredicto ronda 2: PASSED.** El único HIGH tiene mitigación real plegada al spec, respaldada por un
requisito del PRD y con tests que la verifican; los cuatro MEDIUM tienen la suya. Los tres LOW quedan
documentados como aceptados: R-06 por decisión explícita del usuario (D-07), R-07 por decisión ya
tomada en el PRD (R-04 del PRD), y R-08 cubierto por el test de migración de Block 1.

Riesgo residual conocido y fuera de alcance, para que quede en el registro y no como sorpresa: no
existe verificación de la dirección de mail nueva (R-10 del PRD), y no existe revocación de sesiones
en todo el sistema (R-02) — ambos son propiedades del proyecto, no de este ticket, y ambos merecen su
propio ticket de seguridad de cuenta.
