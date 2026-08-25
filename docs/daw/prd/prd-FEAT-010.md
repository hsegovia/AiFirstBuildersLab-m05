# PRD FEAT-010: Frontend completo (bingos, descubrimiento, carrito, compra, mis cartones y cuenta)

| Field | Value |
|-------|-------|
| Ticket | FEAT-010 |
| Tracker | none |
| Date | 2026-08-25T22:25:47Z |
| PRD loops | 0 |

## Context and Problem

El backend de BingoCart cubre hoy todo el recorrido funcional del organizador y del comprador —
registro y autenticación de ambos roles, creación y gestión de bingos, descubrimiento y selección
de cartones, carrito con reserva atómica, confirmación de compra, confirmación/cancelación manual
de pago, y (en FEAT-009d, todavía sin mergear a `main`) "mis cartones" y actualización de datos de
cuenta del comprador. Ninguna de esas funcionalidades tiene pantalla: el frontend Angular solo
implementa registro y login de organizador (`frontend/src/app/features/auth/`). El resto del
proyecto — nueve pantallas o flujos distintos — es indistinguible de no existir para cualquiera que
no use la API directamente.

Este ticket construye esas pantallas, contra los contratos de API ya implementados y probados. No
diseña ningún endpoint nuevo salvo uno mínimo (FR-01) que resuelve una laguna que el frontend
expone y que el backend nunca necesitó hasta ahora: saber, tras un refresh de página, qué rol tiene
la sesión activa.

## Goals

- Que un organizador pueda, desde el navegador, crear y gestionar sus bingos y conciliar sus
  ventas de punta a punta, sin tocar la API a mano.
- Que un participante pueda descubrir cartones, armar un carrito, comprarlos y, ya como comprador
  registrado, ver y descargar lo que compró y mantener actualizados sus datos de cuenta.
- Que la sesión (rol organizador/comprador) sobreviva a un refresh de página sin exponer el JWT al
  JavaScript del cliente.

## Functional Requirements

### Backend — soporte mínimo para el frontend

- FR-01: El sistema debe exponer `GET /api/auth/whoami`, autenticado (cualquier rol), que devuelve
  `{Rol, Mail}` leyendo los claims del JWT vigente — sin nuevo estado, sin nueva tabla, solo lectura
  del token ya emitido.

### Frontend — infraestructura transversal

- FR-02: El sistema debe presentar un layout base con navegación que varíe según el estado de
  sesión: anónimo, organizador autenticado, o comprador autenticado — resuelto vía FR-01 al
  arrancar la aplicación.
- FR-03: El sistema debe manejar todos los errores HTTP mediante un interceptor centralizado
  (401 → redirección a login, 403 → mensaje de permiso denegado, 429 → mensaje de límite excedido,
  5xx → mensaje genérico), nunca un `.subscribe()` sin manejador de error.
- FR-04: El sistema debe restringir el acceso a las rutas de organizador y a las rutas de comprador
  mediante guards que consulten el rol resuelto por FR-01/FR-02, redirigiendo a login si no
  corresponde.

### Organizador — gestión de bingos

- FR-05: El sistema debe permitir a un organizador autenticado crear un bingo indicando nombre del
  evento, fecha y hora del sorteo, cantidad de cartones y costo por cartón.
- FR-06: El sistema debe permitir a un organizador autenticado listar, paginados, los bingos que él
  mismo creó.
- FR-07: El sistema debe permitir a un organizador autenticado editar el nombre del evento, la
  fecha de sorteo y el costo por cartón de un bingo sin compras registradas.
- FR-08: El sistema debe permitir a un organizador autenticado eliminar un bingo sin compras
  registradas, con una confirmación explícita antes de ejecutar la acción.

### Organizador — conciliación de ventas

- FR-09: El sistema debe permitir a un organizador autenticado listar, paginadas, las compras
  generadas sobre sus bingos, sin exponer datos del comprador en el listado.
- FR-10: El sistema debe permitir a un organizador autenticado confirmar manualmente el pago de una
  compra propia en estado pendiente.
- FR-11: El sistema debe permitir a un organizador autenticado cancelar una compra propia en estado
  pendiente.

### Comprador — descubrimiento y selección

- FR-12: El sistema debe presentar un directorio público, paginado, de organizadores con evento
  activo, accesible sin autenticación.
- FR-13: El sistema debe presentar al participante 5 cartones aleatorios de cualquier organizador
  (descubrimiento global), sin requerir autenticación.
- FR-14: El sistema debe permitir al participante seleccionar un organizador del directorio y ver 5
  cartones de ese organizador (descubrimiento por organizador), sin requerir autenticación.
- FR-15: El sistema debe permitir al participante agregar cartones presentados a su carrito y
  quitarlos, sin requerir autenticación.
- FR-16: El sistema debe permitir al participante descartar la tanda actual y pedir una tanda nueva
  de 5 cartones, sin repetir cartones ya agregados al carrito ni ya descartados.

### Carrito

- FR-17: El sistema debe mostrar el carrito acumulado del participante, con el detalle de cada
  cartón, la cantidad total y el monto total.
- FR-18: El sistema debe permitir al participante eliminar un cartón individual de su carrito antes
  de confirmar la compra.

### Checkout (confirmación de compra)

- FR-19: El sistema debe exigir que el participante inicie sesión o se registre como comprador
  antes de habilitar la confirmación de la compra, sin interrumpir ni descartar el carrito ya
  armado.
- FR-20: El sistema debe permitir al comprador autenticado confirmar la compra de su carrito
  indicando el medio de pago (Efectivo o Transferencia).
- FR-21: El sistema debe presentar, tras una confirmación exitosa, el resultado agrupado por
  organizador (una compra por organizador), con el detalle de los cartones de cada una.
- FR-22: El sistema debe permitir el registro de un nuevo comprador (apellido, nombre, CUIT, mail,
  contraseña) desde el flujo de checkout.

### Comprador — mis cartones y cuenta

- FR-23: El sistema debe permitir al comprador autenticado ver, paginados, los cartones que tiene
  adquiridos, en cualquier estado de compra.
- FR-24: El sistema debe permitir al comprador autenticado descargar el PDF de cualquiera de sus
  cartones adquiridos.
- FR-25: El sistema debe permitir al comprador autenticado actualizar sus datos de cuenta
  (apellido, nombre, CUIT, mail), exigiendo su contraseña actual como confirmación.

## Non-Functional Requirements

- NFR-01: Ningún dato de sesión sensible (contraseña, JWT) se persiste en `localStorage` ni
  `sessionStorage` — la sesión sobrevive a un refresh exclusivamente vía la cookie `httpOnly` y
  FR-01.
- NFR-02: La validación de formularios en cliente refleja, sin duplicar como regla de negocio
  propia, las mismas reglas que el backend ya aplica (CUIT de 11 dígitos con dígito verificador,
  formato de mail, campos requeridos) — el mensaje de error mostrado se deriva del código que el
  backend ya devuelve (`CuitInvalido`, `MailEnUso`, etc.), no de una reimplementación paralela.
- NFR-03: Las pantallas son usables en viewport móvil (≥360px de ancho), coherente con que el stack
  ya incluye Tailwind CSS y Angular Material MD3.
- NFR-04: Los cuatro listados paginados del sistema (bingos propios, compras propias, mis cartones,
  directorio) comparten un único componente de paginación, no cuatro implementaciones distintas.
- NFR-05: Cada pantalla que dependa de un endpoint con rate limiting (`carrito`, `compras`,
  `comprador-cuenta`, `bingos`, `registro`, `directorio`, `descubrimiento`) maneja explícitamente el
  429 con un mensaje al usuario, no un error genérico o una pantalla en blanco.

## Acceptance Criteria

*(EARS — ver `.daw/rules/validation-rules.instructions.md` §1)*

- AC-01: WHEN un organizador autenticado envía el formulario de creación de bingo con datos
  válidos, THE sistema SHALL crear el bingo y navegar al listado de bingos propios mostrándolo
  (FR-05).
- AC-02: IF la creación de un bingo falla por `CantidadCartonesExcedeLimite`, `FechaSorteoInvalida`
  o `CostoPorCartonInvalido`, THEN THE sistema SHALL mostrar el mensaje de error específico sin
  perder los datos ya ingresados en el formulario (FR-05).
- AC-03: WHEN un organizador autenticado abre el listado de bingos propios, THE sistema SHALL
  mostrarlos paginados con la paginación de NFR-04 (FR-06).
- AC-04: IF un organizador intenta eliminar un bingo con compras registradas, THEN THE sistema
  SHALL mostrar el motivo del rechazo (409 `BingoConCompras`) sin ejecutar ninguna eliminación
  (FR-08).
- AC-05: WHEN un organizador autenticado confirma el pago de una compra pendiente propia, THE
  sistema SHALL reflejar el nuevo estado "Confirmado" en el listado sin recargar la página completa
  (FR-10).
- AC-06: IF un organizador intenta confirmar o cancelar una compra que no está en estado pendiente,
  THEN THE sistema SHALL mostrar el 409 `EstadoInvalido` como mensaje, sin cambiar el estado
  mostrado (FR-10, FR-11).
- AC-07: WHEN un visitante anónimo abre el directorio público, THE sistema SHALL mostrar los
  organizadores con evento activo, paginados, sin pedir autenticación (FR-12).
- AC-08: WHEN un visitante anónimo pide una nueva tanda de descubrimiento, THE sistema SHALL
  mostrar hasta 5 cartones nuevos que no estén ya en su carrito ni entre los ya descartados en esa
  sesión (FR-16).
- AC-09: WHEN un visitante anónimo agrega un cartón presentado a su carrito, THE sistema SHALL
  reflejar el cartón en el carrito y permitir seguir descubriendo sin perder la selección previa
  (FR-15).
- AC-10: IF un cartón ya fue reservado por otra sesión al intentar agregarlo (409), THEN THE
  sistema SHALL informar que el cartón ya no está disponible y quitarlo de la tanda mostrada
  (FR-15).
- AC-11: WHEN un visitante con carrito no vacío intenta confirmar la compra sin sesión de
  comprador, THE sistema SHALL ofrecerle iniciar sesión o registrarse sin descartar el carrito
  armado (FR-19).
- AC-12: WHEN un comprador autenticado confirma la compra de un carrito no vacío con un medio de
  pago válido, THE sistema SHALL mostrar el resultado agrupado por organizador con el detalle de
  cartones de cada compra (FR-20, FR-21).
- AC-13: IF la confirmación de compra falla por `CarritoVacio` (400), THEN THE sistema SHALL
  mostrar el mensaje y ofrecer volver al descubrimiento, sin intentar reenviar la confirmación
  (FR-20).
- AC-14: IF la confirmación de compra falla por `ReservaCarritoInvalida` (409), THEN THE sistema
  SHALL indicar cuáles cartones ya no están disponibles (usando `cartonIdsInvalidos`) y actualizar
  el carrito mostrado para quitarlos (FR-20).
- AC-15: WHEN un comprador autenticado abre "mis cartones", THE sistema SHALL listar, paginados,
  todos sus cartones adquiridos en cualquier estado de compra, incluidas las canceladas (FR-23).
- AC-16: WHEN un comprador autenticado pide el PDF de un cartón propio, THE sistema SHALL
  descargarlo con el `Content-Type` `application/pdf` recibido (FR-24).
- AC-17: WHEN un comprador autenticado envía el formulario de datos de cuenta con la contraseña
  actual correcta y datos válidos, THE sistema SHALL actualizar los datos mostrados y reflejar el
  éxito sin pedir un nuevo login (FR-25).
- AC-18: IF la actualización de datos de cuenta falla por `ContrasenaIncorrecta` (403), THEN THE
  sistema SHALL mostrar el error sin limpiar los campos de apellido/nombre/CUIT/mail ya ingresados,
  y sin revelar si el mail o el CUIT ingresados colisionan con otra cuenta (FR-25).
- AC-19: IF la actualización de datos de cuenta falla por `PlazoModificacionVencido` (409), THEN
  THE sistema SHALL explicar que hay un sorteo dentro de la próxima hora y no permitir reintentar
  el mismo envío sin recargar el estado (FR-25).
- AC-20: WHEN la sesión se refresca (recarga de página) con una cookie de autenticación vigente,
  THE sistema SHALL restaurar el layout correspondiente al rol (organizador o comprador) vía FR-01,
  sin redirigir a login (FR-01, FR-02, FR-04).
- AC-21: IF la cookie de autenticación no existe o expiró al refrescar una ruta protegida, THEN THE
  sistema SHALL redirigir a la pantalla de login correspondiente, preservando la ruta de destino
  para volver tras autenticarse (FR-04).
- AC-22: WHEN un organizador autenticado edita un bingo sin compras registradas con datos válidos,
  THE sistema SHALL actualizar el bingo y reflejarlo en el listado (FR-07).
- AC-23: WHEN un organizador autenticado abre el listado de sus compras, THE sistema SHALL
  mostrarlas paginadas sin ningún dato del comprador en la respuesta ni en la pantalla (FR-09).
- AC-24: WHEN un visitante anónimo abre el descubrimiento global, THE sistema SHALL mostrar hasta 5
  cartones aleatorios de cualquier organizador con stock disponible (FR-13).
- AC-25: WHEN un visitante anónimo selecciona un organizador del directorio, THE sistema SHALL
  mostrar hasta 5 cartones de ese organizador (FR-14).
- AC-26: WHEN un visitante abre su carrito, THE sistema SHALL mostrar cada cartón agregado, la
  cantidad total y el monto total (FR-17).
- AC-27: WHEN un visitante elimina un cartón individual de su carrito, THE sistema SHALL quitarlo
  del carrito mostrado sin afectar a los demás cartones ya agregados (FR-18).
- AC-28: WHEN un visitante completa el formulario de registro de comprador con datos válidos, THE
  sistema SHALL crear la cuenta y continuar el flujo de checkout ya autenticado (FR-22).
- AC-29: IF cualquier llamada a la API devuelve 429, THEN THE sistema SHALL mostrar un mensaje de
  límite excedido sin dejar la pantalla en blanco ni la acción en un estado indefinido (FR-03).

## Out of Scope

- **Dashboard de ventas del organizador (RF-22, RF-23, RF-24)** — cantidad de vendidos vs. totales,
  listado con datos del comprador y desglose por medio de pago. El backend no existe todavía
  (`docs/daw/prd/prd-FEAT-009d.md`, Out of Scope). Este ticket sí construye el listado mínimo de
  compras del organizador (FR-09), que es distinto: sin datos del comprador ni desglose por medio
  de pago.
- **Validación de cartón por GUID (RF-06)** — sin ticket propio, backend no implementado.
- **Cambio de contraseña del comprador u organizador** — ninguno de los dos tiene ese flujo en el
  backend.
- **Reenvío manual del mail de confirmación** — el backend no lo expone.
- **Cualquier pantalla de administración interna, analytics o reporting** más allá de lo enumerado
  arriba.
- **Internacionalización / soporte multi-idioma** — la aplicación es en español, sin selector de
  idioma.
- **Modo oscuro** — no está en el alcance de este ticket a menos que Angular Material MD3 lo traiga
  por defecto sin trabajo adicional.
- **Tests E2E de Playwright para las pantallas nuevas** — el proyecto ya tiene Playwright para .NET
  configurado para flujos de organizador (`backend/tests/BingoCart.E2E.Tests`); extenderlo a los
  flujos nuevos de este ticket es una decisión de PLAN, no un requisito cerrado acá.

## Risks and Mitigations

- **R-01 — El alcance es grande (27 FR sobre 9 flujos).** Mitigación: el usuario ya decidió
  mantenerlo como un único PRD (igual que FEAT-001/008/009) y dejar que el scope check de PLAN
  decida la partición en sub-tickets. Documentado como decisión D-01.
- **R-02 — FR-01 es la única pieza de backend de un ticket clasificado como frontend.** Es un
  endpoint de solo lectura sobre el JWT ya emitido, sin nuevo estado ni migración — bajo riesgo,
  pero PLAN debe confirmar que no introduce una superficie de autorización nueva (ej. que no exponga
  el JWT completo, solo `Rol`/`Mail`).
- **R-03 — La cookie de autenticación es compartida entre organizador y comprador.** El frontend
  depende de FR-01 para distinguir el rol; si `whoami` fallara o quedara desalineado con el JWT real,
  un usuario podría ver temporalmente la navegación equivocada hasta que una llamada protegida
  devuelva 401/403. La autorización real la sigue haciendo el backend en cada endpoint — FR-01 solo
  maneja UI, nunca reemplaza el chequeo de rol del servidor.
- **R-04 — Cuatro políticas de rate limiting distintas (`carrito`, `compras`, `comprador-cuenta`,
  `bingos`, `registro`, `directorio`, `descubrimiento`) con límites diferentes.** NFR-05 exige
  manejarlas todas, pero el mensaje de "límite excedido" debe evitar sugerir un tiempo de espera
  específico que el frontend no conoce (el backend no expone `Retry-After` hoy) — mensaje genérico,
  sin cuenta regresiva.

## Dependencies

- **FEAT-009d** (mis cartones, actualización de cuenta, correlativo) — mergeado en la rama de
  trabajo de este ticket vía `feat/FEAT-009d-mis-cartones` como base, pero su PR #14 todavía no está
  en `main`. Este ticket no puede cerrarse (mergear) antes de que FEAT-009d se mergee.
- El resto del backend (FEAT-001 a FEAT-009c) ya está en `main`.
- Angular 18, Angular Material 18 (MD3), Tailwind CSS — ya configurados en `frontend/`.
