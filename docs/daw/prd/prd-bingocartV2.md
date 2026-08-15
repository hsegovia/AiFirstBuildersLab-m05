# PRD-001: Carrito de Compras de Bingos v2.0 — Plataforma web para que organizadores vendan cartones de bingo online con control de stock

---

## Contexto y Problema

Los organizadores de bingos (clubes, asociaciones, entidades benéficas) carecen de un canal digital para vender cartones de bingo. La venta se realiza de forma presencial o por grupos de WhatsApp, lo que genera:

- **Baja visibilidad**: los potenciales participantes no conocen qué bingos están disponibles ni de qué organizador son.
- **Sin control de stock**: no hay garantía de que un cartón vendido no se re-venda a otra persona.
- **Friction de compra**: el comprador debe contactar manualmente al organizador, sin descubrimiento ni selección asistida.

**Para quién**:
- **Organizadores** que quieren publicar y vender sus bingos online.
- **Participantes/Compradores** que quieren descubrir y comprar cartones de bingo de forma rápida y segura.

---

## Objetivos

| # | Objetivo | Métrica de éxito |
|---|----------|------------------|
| O1 | Permitir a un organizador publicar un bingo con cartones en menos de 5 minutos | Tiempo de publicación < 5 min |
| O2 | Permitir a un comprador seleccionar y confirmar la compra de cartones en menos de 2 minutos (flujo feliz: una sola tanda de selección, sin contar reintentos) | Tiempo de compra < 2 min en flujo feliz |
| O3 | Garantizar que ningún cartón vendido pueda ser comprado por otro participante | 0% superposición de cartones vendidos |
| O4 | Ofrecer al menos 2 mecanismos de descubrimiento de cartones | 2 métodos de búsqueda operativos al launch |

---

## Requerimientos Funcionales (RF)

### Organizador

| ID | Requerimiento |
|----|---------------|
| RF-01 | El sistema debe permitir a un visitante registrarse como organizador indicando nombre de la organización, CUIT, mail, teléfono y contraseña. |
| RF-01b | El sistema debe permitir a un organizador registrado autenticarse con mail y contraseña. |
| RF-02 | El sistema debe permitir a un organizador crear un bingo indicando nombre del evento, fecha y hora del sorteo, cantidad máxima de cartones (≤ 5.000) y costo de cada cartón (en pesos argentinos, ARS). |
| RF-02b | El sistema debe permitir a un organizador autenticado listar los bingos que él mismo creó, mostrando nombre del evento, fecha y hora del sorteo, cantidad de cartones y costo por cartón. |
| RF-03 | El sistema debe rechazar la creación de un bingo cuya cantidad de cartones supere 5.000. |
| RF-04a | El sistema debe generar, al crear el bingo, tantos cartones como la cantidad indicada por el organizador, cada uno con 10 números aleatorios únicos entre 1 y 90. |
| RF-04b | El sistema debe asignar a cada cartón un GUID único que permita su identificación y validación (ver RF-06). |
| RF-04c | El sistema no debe generar, dentro de un mismo bingo, dos cartones con el mismo conjunto exacto de 10 números. |
| RF-05 | El sistema debe listar en un directorio público únicamente a los organizadores que tengan un evento activo (con stock disponible y fecha de sorteo vigente), mostrando nombre y evento activo. |
| RF-06 | El sistema debe permitir al organizador validar, mediante el GUID de un cartón, que el mismo fue efectivamente vendido a través de la plataforma. |
| RF-25 | El sistema debe permitir al organizador editar el nombre del evento, la fecha y hora del sorteo y el costo por cartón de un bingo que no tenga ninguna compra registrada (pendiente o confirmada). |
| RF-26 | El sistema debe rechazar la edición o eliminación de un bingo que tenga al menos una compra registrada (pendiente o confirmada). |
| RF-27 | El sistema debe permitir al organizador eliminar un bingo que no tenga ninguna compra registrada (pendiente o confirmada). |

### Participante / Comprador

| ID | Requerimiento |
|----|---------------|
| RF-07 | El sistema debe presentar al participante 5 cartones aleatorios provenientes de cualquier organizador (Método de búsqueda 1 — Descubrimiento), sin requerir registro ni inicio de sesión. |
| RF-08 | El sistema debe permitir al participante seleccionar un organizador del listado y presentarle 5 cartones de ese organizador (Método de búsqueda 2 — Por organizador), sin requerir registro ni inicio de sesión. |
| RF-09 | El sistema debe permitir al participante agregar cartones presentados a un carrito de compras individual, seleccionando de 0 a 5 por tanda, sin requerir registro ni inicio de sesión. |
| RF-09b | El sistema debe identificar al participante no registrado mediante un identificador de sesión (cookie o token) persistente en el navegador, para sostener el carrito y el historial de cartones descartados (RF-10) entre tandas sucesivas, sin requerir registro. Si el participante cambia de dispositivo o elimina las cookies del navegador, pierde el carrito y el historial de descartes acumulado. |
| RF-10 | El sistema debe permitir al participante descartar los cartones no seleccionados de la tanda actual y solicitar una nueva tanda de 5 cartones, sin repetir cartones ya agregados al carrito ni cartones ya descartados en tandas anteriores, mientras haya stock disponible, sin límite de reintentos. |
| RF-11 | El sistema debe mantener un carrito por participante que acumule todos los cartones seleccionados en tandas sucesivas, con visibilidad del total de cartones acumulados y monto total. |
| RF-12 | El sistema debe permitir al participante eliminar cartones individuales de su carrito antes de confirmar la compra, sin que esta acción reinicie el plazo de reserva de 5 minutos (RF-13a) de los cartones restantes. |
| RF-13a | El sistema debe reservar el carrito completo por 5 minutos desde el último cartón agregado. |
| RF-13b | El sistema debe reiniciar el plazo de reserva de 5 minutos para todos los cartones del carrito cada vez que se agrega un nuevo cartón. |
| RF-13c | El sistema debe liberar todos los cartones del carrito si la compra no se confirma dentro del plazo de reserva vigente. |
| RF-14 | El sistema debe exigir que el participante se registre e inicie sesión con mail y contraseña recién al momento de confirmar la compra; la navegación, selección de cartones y armado del carrito (RF-07 a RF-13c) no requieren registro. |
| RF-15 | El sistema debe requerir, al confirmar la compra, los datos del comprador: apellido, nombre, CUIT, mail y medio de pago. |
| RF-16 | El sistema debe ofrecer dos medios de pago: Efectivo, Transferencia bancaria. El comprador selecciona un único medio de pago por confirmación de carrito. |
| RF-28 | El sistema debe rechazar la confirmación de compra si el carrito está vacío. |
| RF-17a | El sistema debe agrupar, al confirmar la compra, los cartones del carrito por organizador, generando una compra independiente por cada organizador con su propio ID. |
| RF-17b | El sistema debe asignar a cada compra generada sus cartones correspondientes, los datos del comprador (apellido, nombre, CUIT, mail), una marca temporal y el medio de pago seleccionado por el comprador (RF-16), quedando en estado **«pendiente de confirmación de pago»** como unidad completa (todos sus cartones comparten el mismo estado). El medio de pago es el mismo para todas las compras generadas en una misma confirmación de carrito. |
| RF-17c | El sistema debe permitir al organizador confirmar manualmente la recepción del pago de su propia compra para que esta pase a estado **«confirmado»**, de forma independiente de otras compras generadas en la misma confirmación de carrito. |
| RF-17d | El sistema debe permitir al organizador cancelar manualmente, desde su dashboard, una compra en estado «pendiente de confirmación de pago». |
| RF-17e | El sistema debe liberar los cartones asociados a una compra cancelada para que vuelvan a estar disponibles para la venta. |
| RF-17f | El sistema debe enviar un mail al comprador notificando la cancelación de su compra. |
| RF-18 | El sistema debe anular todo cartón comprado —esté en estado pendiente o confirmado— para que no esté disponible en futuras selecciones o ventas. |
| RF-19a | El sistema debe enviar al comprador un único correo de confirmación que liste todas las compras generadas en esa confirmación de carrito, agrupadas por organizador, con el detalle de los cartones de cada compra (números de cada cartón, organizador e ID de compra). |
| RF-19b | El sistema debe adjuntar al correo de confirmación los cartones de todas las compras de esa confirmación en formato PDF. |
| RF-29 | El sistema debe reintentar el envío del correo de confirmación hasta 3 veces en caso de falla, y marcarlo como fallido si los 3 intentos fallan. |
| RF-20a | El sistema debe permitir al comprador ver qué cartones tiene adquiridos desde la plataforma en todo momento. |
| RF-20b | El sistema debe permitir al comprador generar el PDF de cualquiera de sus cartones adquiridos. |
| RF-21 | El sistema debe permitir al comprador actualizar los datos de su cuenta (apellido, nombre, CUIT, mail), siempre que ninguna de sus compras tenga el sorteo del bingo correspondiente (RF-02) en menos de 1 hora. |
| RF-21b | El sistema debe rechazar la actualización de los datos de cuenta del comprador si tiene al menos una compra cuyo sorteo (RF-02) sea en menos de 1 hora. |

### Dashboard del Organizador

| ID | Requerimiento |
|----|---------------|
| RF-22 | El sistema debe proveer al organizador un dashboard que muestre, por cada bingo publicado: cantidad de cartones vendidos vs. totales. |
| RF-23 | El sistema debe mostrar en el dashboard el listado de cartones vendidos con número de cartón, estado de pago (pendiente/confirmado) y datos del comprador (apellido, nombre, CUIT completo). |
| RF-24 | El sistema debe mostrar en el dashboard un desglose de ventas por medio de pago (Efectivo, Transferencia) con cantidad de operaciones y monto acumulado por medio, discriminando entre pagos pendientes y confirmados. |

---

## Requerimientos No Funcionales (RNF)

| ID | Requerimiento | Métrica |
|----|---------------|---------|
| RNF-01 | La generación de hasta 5.000 cartones debe completarse en menos de 10 segundos | Tiempo de generación < 10 s p95 |
| RNF-02 | La presentación de 5 cartones aleatorios debe responder en menos de 3 segundos | Latencia de respuesta < 3 s p95 |
| RNF-03 | El sistema debe garantizar que la compra de un cartón sea atómica: no se puede asignar el mismo cartón a dos compradores | Fuertemente consistente, 0 asignaciones duplicadas, aplicado en < 1 s |
| RNF-04 | Los datos personales (CUIT, mail, nombre, apellido) deben almacenarse de forma persistente y recuperable en base de datos relacional con acceso restringido por rol (Organizador: acceso solo a sus propios bingos, cartones y compradores; Participante/Comprador: acceso solo a sus propias compras) | 100% de los datos persistidos en SQL Server; 0 accesos exitosos de un Organizador a bingos/cartones/compradores de otro Organizador y 0 accesos exitosos de un Participante a compras de otro Participante, verificado por pruebas de control de acceso |
| RNF-05 | El sistema debe mantener disponibilidad del 99,5% mensual | Downtime < 3,6 h/mes |
| RNF-06 | La interfaz debe ser responsive y funcional en dispositivos móviles (viewport ≥ 320 px) | 100% de funcionalidades accesibles en mobile |
| RNF-07 | Los números de cada cartón deben ser generados con un generador de números pseudo-aleatorios criptográficamente seguro | Uso exclusivo de `System.Security.Cryptography.RandomNumberGenerator` (CSPRNG del .NET Core 8, ver AGENTS.md); 0 usos de `System.Random` u otro generador no criptográfico, verificable por revisión de código |
| RNF-08 | El dashboard del organizador debe actualizar métricas de ventas con latencia menor a 30 segundos desde el registro de la compra (envío del formulario por el comprador, RF-17b), independientemente de la confirmación manual de pago | Latencia dashboard < 30 s |
| RNF-09 | Los datos personales del comprador (CUIT, nombre, apellido, mail) deben tratarse conforme a la Ley 25.326 de Protección de Datos Personales (Argentina): acceso restringido por rol y resguardo de confidencialidad | Checklist verificable, 2/2 ítems cumplidos: (1) acceso restringido por rol (mismo control que RNF-04); (2) datos personales no expuestos en logs ni en respuestas de API a roles sin permiso |

---

## Criterios de Aceptación (AC)

**AC-01 (RF-02, RF-04a, RF-05) — Creación de bingo con cartones válidos**
> Dado un organizador autenticado, cuando crea un bingo con 1.000 cartones a $500 cada uno, entonces el sistema genera exactamente 1.000 cartones con 10 números aleatorios únicos entre 1 y 90 y el bingo aparece en el directorio público.

**AC-01b (RF-04b) — Unicidad de GUID entre cartones**
> Dado un bingo creado con 1.000 cartones, entonces los 1.000 GUIDs generados son todos distintos entre sí.

**AC-01c (RF-04c) — No repetición de conjuntos de números dentro de un bingo**
> Dado un bingo creado con 1.000 cartones, entonces no existen dos cartones dentro de ese bingo con el mismo conjunto exacto de 10 números.

**AC-01d (RF-02b) — Listado de bingos propios del organizador**
> Dado un organizador autenticado con 2 bingos creados, cuando accede a «Mis bingos», entonces el sistema muestra el listado de sus 2 bingos con nombre del evento, fecha y hora del sorteo, cantidad de cartones y costo por cartón, sin mostrar bingos de otros organizadores.

**AC-02 (RF-03) — Rechazo de bingo con más de 5.000 cartones**
> Dado un organizador autenticado, cuando intenta crear un bingo con 5.001 cartones, entonces el sistema rechaza la operación y muestra mensaje de error indicando el límite de 5.000.

**AC-03 (RF-07) — Búsqueda por descubrimiento (Método 1)**
> Dado un participante en la plataforma, cuando selecciona «Descubrir bingos», entonces el sistema presenta 5 cartones aleatorios de cualquier organizador, cada uno mostrando sus 10 números, el nombre del organizador y el costo.

**AC-04 (RF-08) — Búsqueda por organizador (Método 2)**
> Dado un participante en la plataforma, cuando selecciona el organizador «Club Gimnasia de Jujuy», entonces el sistema presenta 5 cartones de ese organizador, cada uno con sus 10 números y costo.

**AC-05a (RF-15, RF-17a, RF-17b) — Registro de compra en estado pendiente**
> Dado un participante con 3 cartones acumulados en su carrito, todos del mismo organizador, provenientes de 2 tandas distintas, cuando completa el formulario con apellido, nombre, CUIT, mail y selecciona «Transferencia» como medio de pago, entonces el sistema registra una compra con los 3 cartones a su nombre en estado «pendiente de confirmación de pago».

**AC-05b (RF-18) — Anulación de cartones tras la compra**
> Dado el registro de la compra del escenario anterior (AC-05a), entonces los 3 cartones quedan anulados y no disponibles para futuras selecciones o ventas.

**AC-05c (RF-19a) — Mail de confirmación de compra**
> Dado el registro de la compra del escenario anterior (AC-05a), entonces el sistema envía al comprador un mail de confirmación con el detalle completo de los 3 cartones.

**AC-05d (RF-17c) — Confirmación manual de pago por el organizador**
> Dado una compra en estado «pendiente de confirmación de pago» (AC-05a), cuando el organizador confirma manualmente la recepción del pago, entonces la compra pasa a estado «confirmado».

**AC-05e (RF-16) — Compra con medio de pago Efectivo**
> Dado un participante con cartones en su carrito, cuando completa el formulario de compra seleccionando «Efectivo» como medio de pago, entonces el sistema registra la compra con medio de pago «Efectivo» en estado «pendiente de confirmación de pago».

**AC-05f (RF-19b) — Adjunto de cartones en PDF en el mail de confirmación**
> Dado el registro de la compra del escenario AC-05a, entonces el mail de confirmación incluye como adjunto un archivo PDF por cada uno de los 3 cartones comprados.

**AC-06 (RF-18) — Cartón comprado no disponible**
> Dado un cartón que fue comprado por el Participante A (en estado pendiente o confirmado), cuando cualquier otro participante solicita cartones (por cualquier método de búsqueda), entonces ese cartón no aparece entre las opciones presentadas.

**AC-07 (RF-28) — Carrito vacío no permite confirmar compra**
> Dado un participante con carrito vacío, cuando intenta confirmar la compra, entonces el sistema no permite avanzar y muestra mensaje indicando que debe agregar al menos un cartón al carrito.

**AC-08 (RF-10) — Regeneración de cartones**
> Dado un participante que no está satisfecho con los 5 cartones presentados, cuando solicita nuevos cartones, entonces el sistema presenta una tanda nueva de 5 cartones diferentes, sin repetir los ya agregados al carrito ni los ya mostrados y descartados en tandas anteriores.

**AC-09 (RF-11) — Acumulación en carrito desde múltiples tandas**
> Dado un participante que seleccionó 1 cartón de la primera tanda de 5 y 2 cartones de una segunda tanda de 5, cuando revisa su carrito, entonces el sistema muestra 3 cartones acumulados con sus números, organizador, costo individual y costo total.

**AC-10 (RF-13a, RF-13b, RF-13c) — Reserva de cartón con vencimiento a 5 minutos**
> Dado un participante que agregó un cartón a su carrito, cuando pasan 5 minutos sin confirmar la compra, entonces el cartón se libera del carrito y vuelve a estar disponible para otros participantes. Cada vez que se agrega un cartón al carrito se reinician los 5 minutos para la reserva de todo el carrito.

**AC-11 (RF-12) — Eliminación de cartón del carrito**
> Dado un participante con 3 cartones en su carrito, cuando elimina 1 cartón, entonces el carrito muestra 2 cartones y el monto total se recalcula; el cartón eliminado vuelve a estar disponible para otros participantes.

**AC-11b (RF-12) — Eliminar un cartón no reinicia la reserva de los restantes**
> Dado un participante con 3 cartones en su carrito cuya reserva vence en 2 minutos, cuando elimina 1 cartón del carrito, entonces la reserva de los 2 cartones restantes sigue venciendo en el mismo instante original, sin reiniciarse a 5 minutos.

**AC-12a (RF-22) — Conteo de vendidos vs. total**
> Dado un organizador con un bingo de 5.000 cartones y 200 vendidos, cuando accede a su dashboard, entonces ve «200/5.000 vendidos».

**AC-12b (RF-23) — Listado de ventas con datos del comprador**
> Dado un organizador con un bingo que tiene 200 cartones vendidos a $500 cada uno, cuando accede a su dashboard, entonces ve el listado de las 200 ventas con número de cartón, estado de pago (pendiente/confirmado), apellido, nombre y CUIT completo del comprador.

**AC-12c (RF-24) — Desglose de ventas por medio de pago**
> Dado un organizador con 150 ventas por Transferencia y 50 por Efectivo a $500 cada cartón, cuando accede a su dashboard, entonces ve el desglose: Transferencia $75.000 (150 ops), Efectivo $25.000 (50 ops).

**AC-13 (RF-08) — Cartones agotados de un organizador**
> Dado un organizador cuyos cartones se agotaron, cuando un participante intenta ver sus cartones (Método 2), entonces el sistema informa que ese bingo se quedó sin stock disponible.

**AC-14a (RF-29) — Falla de SMTP tras agotar reintentos**
> Dado un envío de correo de confirmación que falla, cuando se agotan 3 reintentos de envío, entonces el sistema marca ese envío como fallido y no continúa reintentando.

**AC-14b (RF-20a) — Descarga de cartones pese a falla de mail**
> Dado un comprador cuyo correo de confirmación falló tras agotar los reintentos (AC-14a), cuando ingresa a su cuenta, entonces puede descargar igualmente sus cartones.

**AC-15 (RF-07) — Sin cartones disponibles en toda la plataforma**
> Dado que ningún organizador tiene cartones disponibles, cuando un participante selecciona «Descubrir bingos» (Método 1), entonces el sistema muestra el mensaje «No hay cartones disponibles en este momento» en lugar de una pantalla vacía o un error.

**AC-16 (RF-05) — Organizador sin evento activo oculto del directorio**
> Dado un organizador cuyo bingo agotó todos sus cartones o cuya fecha de sorteo ya pasó, cuando un participante consulta el directorio público (RF-05), entonces ese organizador no aparece en el listado.

**AC-17 (RF-17a, RF-17b) — Compra dividida por organizador**
> Dado un participante con 4 cartones en su carrito, 3 del Organizador A y 1 del Organizador B, cuando confirma la compra con Transferencia como medio de pago, entonces el sistema genera 2 compras independientes (una por organizador), cada una con su propio ID y ambas con el mismo medio de pago, y envía un único correo de confirmación que detalla ambas compras.

**AC-18 (RF-17d, RF-17e, RF-17f) — Cancelación manual de compra pendiente**
> Dado un organizador con una compra en estado «pendiente de confirmación de pago», cuando la cancela manualmente desde su dashboard, entonces los cartones de esa compra vuelven a estar disponibles para la venta y el sistema envía un mail al comprador notificando la cancelación.

**AC-19 (RF-09b) — Pérdida de carrito por sesión anónima**
> Dado un participante no registrado con cartones en su carrito, cuando elimina las cookies del navegador o accede desde otro dispositivo, entonces el sistema no puede recuperar su carrito ni su historial de cartones descartados, y debe comenzar de nuevo.

**AC-20 (RF-01, RF-01b) — Registro y autenticación de organizador**
> Dado un visitante sin cuenta, cuando se registra como organizador indicando nombre de la organización, CUIT, mail, teléfono y contraseña, entonces el sistema crea la cuenta y le permite iniciar sesión posteriormente con mail y contraseña.

**AC-21 (RF-06) — Validación de cartón por GUID**
> Dado un organizador autenticado con el GUID de un cartón vendido a través de la plataforma, cuando lo consulta en el sistema, entonces el sistema confirma que ese cartón fue efectivamente vendido a través de la plataforma, mostrando número de cartón, organizador e ID de compra.

**AC-22 (RF-20a, RF-20b) — Ver cartones adquiridos y descargar PDF**
> Dado un comprador autenticado con compras registradas, cuando accede a «Mis cartones», entonces ve el listado completo de cartones adquiridos (número, bingo, organizador, estado de pago) y puede descargar cada cartón en formato PDF.

**AC-23 (RF-21) — Actualización de datos de cuenta dentro del plazo**
> Dado un comprador autenticado cuyas compras registradas tienen todas el sorteo en más de 1 hora, cuando actualiza los datos de su cuenta (apellido, nombre, CUIT, mail), entonces el sistema guarda los cambios exitosamente y estos aplican a todas sus compras.

**AC-24 (RF-21b) — Bloqueo de actualización fuera de plazo**
> Dado un comprador autenticado con al menos una compra cuyo sorteo es en menos de 1 hora, cuando intenta actualizar los datos de su cuenta, entonces el sistema rechaza la operación e informa que el plazo para modificar datos ya venció para esa compra.

**AC-25 (RNF-04) — Control de acceso entre organizadores**
> Dado el Organizador A autenticado, cuando intenta acceder al dashboard, cartones o listado de compradores del Organizador B, entonces el sistema rechaza el acceso y no muestra datos de bingos, cartones ni compradores que no le pertenecen.

**AC-26 (RNF-04) — Control de acceso entre participantes/compradores**
> Dado el Participante/Comprador A autenticado, cuando intenta acceder al historial de compras o datos personales de otro Participante/Comprador B, entonces el sistema rechaza el acceso; A solo puede ver sus propias compras.

**AC-27 (RF-25) — Edición de bingo sin ventas**
> Dado un organizador con un bingo publicado que no tiene ninguna compra registrada, cuando edita el nombre del evento, la fecha y hora del sorteo o el costo por cartón, entonces el sistema guarda los cambios exitosamente.

**AC-28 (RF-26) — Rechazo de edición o eliminación de bingo con ventas**
> Dado un organizador con un bingo que tiene al menos 1 compra registrada (pendiente o confirmada), cuando intenta editarlo o eliminarlo, entonces el sistema rechaza la operación e informa que el bingo no puede modificarse por tener ventas asociadas.

**AC-29 (RF-27) — Eliminación de bingo sin ventas**
> Dado un organizador con un bingo publicado que no tiene ninguna compra registrada, cuando lo elimina, entonces el bingo deja de estar disponible en el directorio público.

**AC-30 (RF-09) — Selección de cartones de la tanda actual**
> Dado un participante con una tanda de 5 cartones presentados, cuando selecciona 3 de ellos y confirma la selección, entonces esos 3 cartones se agregan a su carrito sin haberse registrado ni iniciado sesión.

**AC-31 (RF-14) — Bloqueo de confirmación de compra sin registro**
> Dado un participante no registrado con cartones en su carrito, cuando intenta confirmar la compra sin haberse registrado ni iniciado sesión, entonces el sistema le exige registrarse/iniciar sesión antes de continuar y no registra la compra hasta que lo haga.

---

## Fuera de Alcance

- **Juego en vivo / sorteo del bingo**: no se implementa la ejecución del bingo ni el canto de números; el sistema es solo de venta de cartones. Es una aplicación de comercialización de bingos.
- **Devoluciones o reembolsos**: una compra confirmada no puede cancelarse ni devolverse desde la plataforma.
- **Integración con pasarelas de pago**: los medios de pago (Efectivo, Transferencia) se registran pero no se procesan automáticamente; la conciliación es manual por el organizador.
- **Autenticación con redes sociales / SSO**: solo registro con mail y contraseña.
- **Notificaciones push o SMS**: solo notificación por mail.
- **Asignación de premios o verificación de victorias**: el sistema no verifica si un cartón ganó ni gestiona premios.
- **Soporte multi-idioma**: solo español.
- **Apps nativas (iOS/Android)**: solo versión web responsive.
- **Múltiples bingos simultáneos por organizador**: un organizador puede tener un solo bingo activo a la vez.
- **Modificación de la cantidad de cartones de un bingo ya publicado**: la cantidad de cartones es fija desde la creación (RF-02) y no forma parte de los campos editables (RF-25).
- **Edición o eliminación de un bingo con ventas registradas**: un bingo con al menos 1 compra (pendiente o confirmada) queda inmutable (RF-26).
- **Validación de CUIT contra AFIP en tiempo real**: se registra el CUIT tal cual lo ingresa el usuario, sin validación contra un padrón externo (ver R-05).

---

## Riesgos y Dependencias

| # | Riesgo / Dependencia | Impacto | Mitigación |
|---|---------------------|---------|------------|
| R-01 | **Concurrencia en compra**: dos participantes podrían intentar comprar el mismo cartón simultáneamente | Crítico — duplicación de ventas | Usar transacciones atómicas con lock optimista o pesimista a nivel de cartón (RNF-03) |
| R-02 | **Generación de números no aleatorios**: un ataque podría predecir los números de cartones no vendidos | Alto — fraude posible | Usar CSPRNG (RNF-07) y no exponer IDs secuenciales predecibles |
| R-03 | **Stock agotado**: si un organizador publica 200 cartones y se venden todos, el Método 2 debe informar sin cartones disponibles | Medio — UX degradada | Mostrar mensaje «Sin cartones disponibles» y sugerir otros organizadores |
| R-04 | **Dependencia de servicio de mail**: si el SMTP cae, no se envían confirmaciones | Medio — comprador sin comprobante | Implementar retry con cola; permitir descarga del comprobante desde la plataforma |
| R-05 | **Validación de CUIT**: no se valida contra AFIP en tiempo real | Bajo — posible CUIT falso | Registrar CUIT tal cual; en futuras versiones integrar validación AFIP |
| R-06 | **Escala de cartones**: generar 5.000 × 10 números = 50.000 registros en un solo request puede impactar performance | Medio — timeout | Generar de forma asíncrona con feedback al organizador; RNF-01 establece el SLA |
| R-07 | **Carritos abandonados con reservas**: participantes que agregan cartones y navegan sin confirmar generan reservas de 5 minutos que reducen el stock visible | Medio — cartones temporalmente no disponibles | Timeout de 5 minutos (AC-10); al liberarse vuelven al pool disponible inmediatamente |
| R-08 | **Base de Datos**: Toda la información se debe persistir en una base de datos transaccional | Alto — Servicio de datos no disponible | Debe contemplarse tener alta disponibilidad en el servicio de datos. |
| R-09 | **Compras pendientes sin cancelar**: si el organizador no cancela manualmente una compra impaga, los cartones quedan bloqueados indefinidamente | Medio — stock inmovilizado | RF-17d permite al organizador cancelar manualmente una compra pendiente desde su dashboard, liberando los cartones |
