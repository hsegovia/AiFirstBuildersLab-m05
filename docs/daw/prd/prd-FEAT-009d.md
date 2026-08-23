# PRD FEAT-009d: "Mis cartones" del comprador y actualización de datos de cuenta

| Field | Value |
|-------|-------|
| Ticket | FEAT-009d |
| Tracker | none |
| Date | 2026-08-23 |
| PRD loops | 1 |

## Context and Problem

Hoy el comprador puede registrarse, armar un carrito y confirmar una compra (FEAT-008b, FEAT-009a),
y recibe un mail con sus cartones adjuntos en PDF (FEAT-009b). Pero **una vez cerrada esa pantalla,
no tiene forma de volver a ver qué compró**: la única copia de sus cartones vive en un mail que puede
perderse, terminar en spam, o directamente no llegar — el propio outbox de FEAT-009b contempla que un
envío agote sus 3 reintentos y quede marcado como `Fallido`. En ese escenario el comprador pagó y no
tiene absolutamente nada.

Tampoco puede corregir sus propios datos. Si se equivocó al tipear el apellido o el CUIT al confirmar
la compra, ese error queda congelado en la compra y en el cartón, y el organizador lo va a ver mal
escrito al conciliar el pago. Pero corregirlo no puede ser libre para siempre: cuando el sorteo está
por ocurrir, el organizador ya está trabajando con esos datos para identificar al ganador, y cambiarlos
en ese momento genera exactamente la disputa que el sistema debería evitar.

Este ticket cierra las dos puntas: darle al comprador acceso permanente a lo que compró, independiente
del mail, y permitirle corregir sus datos mientras hacerlo todavía sea seguro.

## Goals

- Que el comprador vea, en todo momento y sin depender del mail, los cartones que adquirió.
- Que pueda descargar el PDF de cualquiera de esos cartones cuando lo necesite.
- Que pueda corregir apellido, nombre, CUIT y mail de su cuenta.
- Que esa corrección quede bloqueada cuando alguno de sus sorteos es inminente, para que los datos con
  los que el organizador identifica al ganador no cambien bajo sus pies.

## Functional Requirements

- FR-01: El sistema debe permitir a un comprador autenticado listar todos los cartones que adquirió a
  través de la plataforma, mostrando por cada uno su número de cartón dentro del bingo, su
  identificador único, sus 10 números, el nombre del bingo, el nombre de la organización y el estado de
  pago de la compra a la que pertenece. *(RF-20a)*
- FR-02: El sistema debe incluir en ese listado los cartones de compras en cualquier estado
  (`PendienteConfirmacionPago`, `Confirmado`, `Cancelado`), sin omitir ninguno. *(RF-20a)*
- FR-03: El sistema debe paginar el listado de cartones del comprador. *(RF-20a)*
- FR-04: El sistema debe permitir a un comprador autenticado descargar en formato PDF cualquier cartón
  que le pertenezca. *(RF-20b)*
- FR-05: El sistema debe rechazar toda consulta o descarga de un cartón que no pertenezca al comprador
  autenticado, sin distinguir ese caso del de un cartón inexistente. *(RNF-04, AC-26)*
- FR-06: El sistema debe permitir a un comprador autenticado actualizar el apellido, el nombre, el CUIT
  y el mail de su cuenta. *(RF-21)*
- FR-07: El sistema debe rechazar la actualización de datos de cuenta si el comprador tiene al menos
  una compra no cancelada cuyo bingo tenga la fecha de sorteo dentro de los 60 minutos siguientes al
  momento de la solicitud. *(RF-21b)*
- FR-08: El sistema debe rechazar la actualización de datos de cuenta si el mail solicitado ya
  pertenece a otra cuenta. *(RF-21)*
- FR-09: El sistema debe validar el CUIT solicitado con las mismas reglas ya vigentes en el registro
  del comprador: 11 dígitos numéricos y dígito verificador válido. *(RF-21)*
- FR-10: El sistema debe rechazar la actualización de datos de cuenta si el CUIT solicitado ya
  pertenece a otra cuenta. *(RF-21)*
- FR-11: El sistema debe asignar a cada cartón, al generarlo, un número correlativo dentro de su bingo,
  comenzando en 1 y sin repetirse entre cartones del mismo bingo. Ese número es exclusivamente de
  presentación: el sistema no debe aceptarlo como identificador en ninguna operación, que sigue
  requiriendo el identificador único del cartón. *(RF-20a, RF-04b)*

## Non-Functional Requirements

- NFR-01: El listado de cartones debe paginarse con un tamaño de página máximo de 50 elementos; una
  solicitud que pida más debe servirse con 50.
- NFR-02: Los tres endpoints de este ticket deben estar limitados a 30 solicitudes por cada 5 minutos
  por comprador autenticado.
- NFR-03: El listado de cartones debe responder en menos de 2 segundos para un comprador con hasta 500
  cartones adquiridos.
- NFR-04: El identificador del comprador debe derivarse exclusivamente del claim de autenticación en
  los 3 endpoints; ninguno debe aceptarlo por ruta, query string ni cuerpo de la solicitud.
- NFR-05: La ventana de bloqueo de FR-07 debe ser de exactamente 60 minutos, evaluada contra la hora
  UTC del servidor al momento de procesar la solicitud.

## Acceptance Criteria

*(EARS — see `.daw/rules/validation-rules.instructions.md` §1 for the five patterns)*

- AC-01: WHEN un comprador autenticado con compras registradas solicita su listado de cartones, THE
  sistema SHALL devolver todos sus cartones adquiridos, cada uno con su número correlativo dentro del
  bingo, su identificador único, sus 10 números, el nombre del bingo, el nombre de la organización y el
  estado de pago de su compra. *(FR-01, RF-20a)*
- AC-02: WHEN un comprador autenticado tiene cartones en compras con distinto estado, THE sistema SHALL
  incluirlos todos en el listado, indicando en cada uno si su compra está pendiente de confirmación,
  confirmada o cancelada. *(FR-02)*
- AC-03: WHEN un comprador autenticado solicita el listado con un tamaño de página mayor a 50, THE
  sistema SHALL devolver como máximo 50 cartones en esa página junto con el total sin paginar.
  *(FR-03, NFR-01)*
- AC-04: WHEN un comprador autenticado solicita el PDF de un cartón que le pertenece, THE sistema SHALL
  devolver un documento PDF con los 10 números de ese cartón y su identificador. *(FR-04, RF-20b)*
- AC-05: IF un comprador autenticado solicita el PDF de un cartón que no le pertenece o que no existe,
  THEN THE sistema SHALL rechazar la solicitud con el mismo error de recurso no encontrado en ambos
  casos, sin revelar cuál de los dos ocurrió. *(FR-05, RNF-04, AC-26)*
- AC-06: WHEN un comprador autenticado cuyas compras no canceladas tienen todas su sorteo a más de 60
  minutos actualiza su apellido, nombre, CUIT y mail, THE sistema SHALL persistir los cambios y
  reflejarlos en las consultas posteriores de su cuenta. *(FR-06, RF-21, AC-23)*
- AC-07: IF un comprador autenticado con al menos una compra no cancelada cuyo bingo sortea dentro de
  los 60 minutos siguientes intenta actualizar sus datos de cuenta, THEN THE sistema SHALL rechazar la
  operación e informar que el plazo para modificar los datos ya venció. *(FR-07, RF-21b, AC-24)*
- AC-08: IF un comprador autenticado intenta actualizar su mail a uno que ya pertenece a otra cuenta,
  THEN THE sistema SHALL rechazar la operación e informar que ese mail ya está en uso, sin modificar
  ninguno de los otros datos enviados. *(FR-08)*
- AC-09: IF un comprador autenticado envía un CUIT que no tiene 11 dígitos numéricos o cuyo dígito
  verificador es inválido, THEN THE sistema SHALL rechazar la operación indicando cuál de las dos
  reglas falló. *(FR-09)*
- AC-10: WHEN un comprador autenticado cuyo mail de confirmación quedó marcado como fallido tras agotar
  sus reintentos solicita su listado de cartones, THE sistema SHALL devolverlo completo y permitir la
  descarga de sus PDF, sin depender del estado del envío de mail. *(FR-01, FR-04, RF-20a, AC-14b)*
- AC-11: IF un comprador autenticado intenta actualizar su CUIT a uno que ya pertenece a otra cuenta,
  THEN THE sistema SHALL rechazar la operación e informar que ese CUIT ya está en uso, sin modificar
  ninguno de los otros datos enviados. *(FR-10)*
- AC-12: WHEN se genera el conjunto de cartones de un bingo de N cartones, THE sistema SHALL asignar a
  cada cartón un número correlativo distinto entre 1 y N dentro de ese bingo. *(FR-11)*
- AC-13: IF una solicitud usa el número correlativo de un cartón en lugar de su identificador único
  para descargar su PDF, THEN THE sistema SHALL rechazarla sin devolver ningún cartón. *(FR-11,
  RNF-07)*

## Decisiones de producto tomadas

**D-01 — Los sorteos ya pasados no bloquean la edición (FR-07).** El PRD maestro dice que se rechaza
la actualización si hay "al menos una compra cuyo sorteo sea en menos de 1 hora", sin aclarar qué pasa
con los sorteos ya ocurridos. Leído literalmente, un sorteo del año pasado también está "a menos de 1
hora" y congelaría los datos de esa cuenta para siempre después de su primera compra. Decisión tomada
por el usuario (hsegovia, 2026-08-23): **solo bloquean los sorteos futuros dentro de los próximos 60
minutos**; los ya pasados se ignoran por completo. La ventana es `[ahora, ahora + 60 min]`, no
`(-∞, ahora + 60 min]`.

**D-02 — Las compras canceladas no bloquean la edición (FR-07).** Consecuencia de D-01 y del criterio
de FEAT-009c: una compra cancelada ya no tiene cartones vigentes, así que su sorteo no puede generar la
disputa que FR-07 previene. Se excluyen del cálculo de la ventana.

**D-03 — El ticket no se splitea, pese a cruzar el guideline de scope.** El assessment obligatorio dio
10 AC (guideline: 5-7) sobre 2 áreas de negocio sin dependencia entre sí — "mis cartones" (FR-01 a
FR-05) es lectura sobre `CompraCartones`/`Cartones`/`Bingos` más el renderer de PDF ya existente, y
"datos de cuenta" (FR-06 a FR-09) es una mutación sobre Identity con una regla temporal contra
`Bingo.FechaSorteoUtc`. Se propuso al usuario el split en a/b y decidió (hsegovia, 2026-08-23)
**mantenerlo entero**: cada mitad por separado es chica y no justifica pagar dos veces el pipeline
completo. Consecuencia asumida para PLAN: los bloques del spec deben respetar esa separación interna
—los de "mis cartones" y los de "datos de cuenta" no comparten archivos— para que el ticket siga siendo
revisable por partes aunque se entregue junto.

**D-04 — Un envío de mail pendiente se entrega al mail nuevo.** El impact scan probó que `EnvioMail`
guarda `CompradorId`, no la dirección, y que el destinatario se resuelve recién al enviar. Con un envío
en cola, cambiar el mail redirige la confirmación de una compra ya hecha a la dirección nueva. Decisión
del usuario (hsegovia, 2026-08-23): **se deja así**, sin congelar el destinatario ni bloquear el
cambio. Es coherente con la intención del comprador —cambió su mail porque quiere recibir ahí— y no
requiere tocar el outbox de FEAT-009b. Queda documentado como comportamiento deliberado, no como
efecto colateral no advertido.

**D-05 — Se reemite la cookie de sesión tras una actualización exitosa.** El JWT lleva el mail en el
claim `Email`; sin reemitir, la cookie vigente seguiría con el mail viejo hasta expirar. La
autorización usa `NameIdentifier`, así que nada se rompe funcionalmente, pero el token quedaría con un
dato falso. Decisión del usuario (hsegovia, 2026-08-23): al actualizar con éxito se emite una cookie
nueva con los claims frescos, reutilizando el mismo mecanismo de emisión del login.

**D-06 — El número correlativo de cartón es de presentación, nunca direccionable (FR-11).** `Carton`
no tenía ningún campo numérico y AC-01 pedía mostrar un "número de cartón". Decisión del usuario
(hsegovia, 2026-08-23): se agrega un correlativo por bingo. Para no violar la prohibición de
`AGENTS.md` de exponer identificadores secuenciales predecibles (RNF-07/R-02), ese número **solo se
muestra**: el identificador único del cartón sigue siendo el único aceptado en cualquier operación
—descarga de PDF incluida— y adivinar un correlativo no da acceso a nada (AC-13). Consecuencia asumida:
el ticket incorpora un cambio de modelo con migración y backfill de los cartones ya existentes, y toca
la generación de cartones de FEAT-003.

## Assumptions

- **A-01:** El listado incluye los cartones de compras canceladas, marcados como tales. AC-22 del PRD
  maestro pide explícitamente "estado de pago" como dato de cada cartón, lo que implica que se listan
  también las compras que no están confirmadas; y un comprador al que le cancelaron una compra necesita
  poder verlo. *(Cubierto por FR-02 y AC-02.)*
- **A-02:** La descarga del PDF de un cartón cuya compra fue cancelada **se permite**. El PRD maestro
  no lo cubre. Se permite porque el PDF es un comprobante de lo que ocurrió, no un título de propiedad
  vigente: el cartón ya volvió a estar disponible para la venta (FEAT-009c) y el estado real lo dicta
  el listado, no el archivo. Negar la descarga escondería evidencia de una operación que existió.
- **A-03:** La actualización de datos es total, no parcial: la solicitud lleva los cuatro campos y los
  cuatro se persisten juntos. RF-21 los enumera como un conjunto.
- **A-04 (corregida en el loop 1):** Los datos actualizados aplican a las compras existentes por
  referencia — la compra apunta al comprador, no copia sus datos —, así que no hay que propagar nada
  hacia las compras. AC-23 se cumple sin trabajo adicional. **Corrección:** la versión original de esta
  assumption decía que nada más se veía afectado, y el impact scan probó que eso era falso para el
  outbox de mail, que resuelve al destinatario en tiempo de envío. Ese caso está ahora cubierto por
  D-04.
- **A-05:** La respuesta de la actualización de datos devuelve el estado ya actualizado de la cuenta,
  de modo que AC-06 ("reflejarlos en las consultas posteriores") se satisface sin agregar un endpoint
  de lectura. El proyecto hoy no tiene un endpoint de perfil del comprador y este ticket no lo crea.

## Out of Scope

- **Dashboard del organizador (RF-22, RF-23, RF-24)** — cantidad de vendidos vs. totales, listado con
  datos del comprador y desglose por medio de pago. Ticket futuro y separado, fuera del split de
  FEAT-009 desde `prd-FEAT-009a.md`.
- **Validación de cartón por GUID por parte del organizador (RF-06)** — fue FEAT-006, abandonado por
  depender del flujo de compra. Ese flujo ya existe, con lo cual quedó desbloqueado, pero es un ticket
  propio con su propio actor.
- **Cambio de contraseña del comprador** — RF-21 enumera apellido, nombre, CUIT y mail. La contraseña
  no está en esa lista y tiene su propio flujo de seguridad (verificación de la contraseña actual,
  invalidación de sesiones) que este ticket no aborda.
- **Cualquier pantalla de frontend** — backend-only, mismo criterio que el resto del roadmap.
- **Reenvío del mail de confirmación fallido** — AC-14b pide que el comprador pueda descargar sus
  cartones pese a la falla, y eso se cumple con FR-01/FR-04. Reintentar o reenviar el mail a pedido es
  otra funcionalidad.
- **Descarga de todos los cartones en un único PDF** — RF-20b habla de "cualquiera de sus cartones",
  uno por vez.

## Risks and Mitigations

- **R-01: Un comprador accede a cartones de otro.** Es el riesgo central del ticket: los dos endpoints
  de lectura reciben identificadores y devuelven datos de compra. Mitigación: el `compradorId` se deriva
  únicamente del claim de autenticación (NFR-04) y la pertenencia del cartón se verifica cruzando
  `CompraCartones` con la compra del comprador; un cartón ajeno responde igual que uno inexistente
  (FR-05, AC-05), sin permitir enumeración.
- **R-02: La ventana de 60 minutos se evalúa con relojes distintos.** Si la comparación usa hora local
  en un punto y UTC en otro, el bloqueo se corre horas. Mitigación: NFR-05 fija UTC del servidor como
  única referencia, coherente con `Bingo.FechaSorteoUtc`, que ya es UTC.
- **R-03: Cambiar el mail rompe el login.** El mail es el identificador de acceso; si se actualiza el
  campo de negocio pero no el de autenticación, el comprador queda sin poder entrar. Mitigación: la
  actualización debe tratarse como un cambio de credencial de Identity y no como un simple campo, y
  FR-08 impide la colisión con otra cuenta.
- **R-04: Race entre la edición y el inicio del sorteo.** Un comprador podría enviar la actualización
  a los 61 minutos y que se procese a los 59. Mitigación: la ventana se evalúa en el servidor al
  procesar (NFR-05), no en el cliente. El margen residual es de segundos y no cambia el resultado
  práctico; se acepta como riesgo menor.
- **R-06: El correlativo de cartón se usa como identificador por error.** FR-11 lo declara de
  presentación, pero una implementación descuidada podría aceptarlo en una ruta y reintroducir la
  enumerabilidad que `AGENTS.md` prohíbe (RNF-07/R-02). Mitigación: AC-13 exige un test explícito de que
  usar el correlativo en lugar del identificador no devuelve ningún cartón, y ninguna firma pública del
  ticket lo recibe como parámetro.
- **R-07: El backfill del correlativo asigna números inestables.** Los cartones existentes en
  producción no tienen correlativo y hay que asignárselo por migración; si el criterio de orden no es
  determinista, dos entornos podrían numerar el mismo bingo distinto. Mitigación: la migración debe
  fijar un orden determinista y documentado, y AC-12 exige que dentro de un bingo los números sean
  distintos y correlativos desde 1.
- **R-05: El listado se degrada con muchos cartones.** Un comprador con cientos de cartones cruzando
  compras, bingos y organizadores puede generar una consulta pesada. Mitigación: paginación obligatoria
  (FR-03, NFR-01) y el techo de rendimiento de NFR-03.

## Dependencies

- **FEAT-009a** (mergeado) — `Compra`, `CompraCartones`, el rol `Comprador` y su autenticación por
  cookie. Sin eso no hay compras que listar ni comprador que autenticar.
- **FEAT-009b** (mergeado) — `ICartonPdfRenderer` y su implementación con QuestPDF. FR-04 la reutiliza
  tal cual; este ticket no construye generación de PDF.
- **FEAT-009c** (mergeado) — el estado `Cancelado` de `Compra` y el criterio de que cancelar no borra
  `CompraCartones`. FR-02 y D-02 dependen de ambas cosas.
- **FEAT-003** (mergeado) — `Bingo.FechaSorteoUtc`, el ancla temporal de FR-07.
- **`CuitValidator`** (FEAT-001a, Domain) — FR-09 reutiliza sus dos reglas sin cambiarlas.
