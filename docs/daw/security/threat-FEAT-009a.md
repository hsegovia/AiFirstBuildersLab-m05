# Threat Model — FEAT-009a (Confirmar compra, núcleo)

| Field | Value |
|-------|-------|
| Ticket | FEAT-009a |
| Date | 2026-08-20 |
| Spec | docs/daw/specs/spec-FEAT-009a.md |

## Attack surfaces identified

1. `POST /api/compradores/registro` — público, primera vez que el proyecto recolecta PII de un rol
   distinto al organizador (Apellido, Nombre, CUIT, Mail, Password).
2. `POST /api/compradores/login` — público.
3. `POST /api/compras/confirmar` — autenticado, primer endpoint que combina DOS sesiones distintas
   en la misma request: el JWT del comprador (`bingocart_auth`) y la sesión anónima del carrito
   (`bingocart_carrito`, Redis) — sin precedente en el proyecto.
4. `AspNetRoles`/`AspNetUserRoles` — provisionadas desde FEAT-001a pero sin ningún uso real hasta
   este ticket. Primer uso real de `[Authorize(Roles = "...")]` en el proyecto.
5. `ApplicationUser` — esquema modificado: `NombreOrganizacion`/`Telefono` pasan a nullable, se
   agregan `Apellido`/`Nombre`. Organizador y comprador comparten la misma tabla y el mismo índice
   único de `Cuit`.
6. `CompraCartones` (`CartonId` `UNIQUE`) — última línea de defensa contra doble venta.
7. Ordenamiento CHECK (Redis, solo lectura) → COMMIT (SQL, transaccional) → RELEASE (Redis, borra) —
   primera vez que el proyecto coordina dos almacenes sin una transacción distribuida real.
8. `DescubrimientoRepository`/`BingoRepository.ObtenerParaCarritoAsync` — ambos ganan una subquery
   `NOT EXISTS` nueva contra `CompraCartones`.
9. `JwtTokenService.GenerarToken` — cambia de firma, agrega el claim `role` al JWT ya emitido para
   organizador desde FEAT-001b.

## Trust boundaries

- **Cliente → Api**: `compradorId` se deriva EXCLUSIVAMENTE del claim `NameIdentifier` del JWT ya
  validado por `AddJwtBearer` (firmado con la misma clave HMAC-SHA256 ya auditada en FEAT-001b) —
  nunca de un parámetro de ruta, query o body. `sesionId` (cookie `bingocart_carrito`) sigue siendo
  un string opaco no firmado del lado del cliente — mismo modelo de confianza ya aceptado en
  FEAT-008b (R-01 de ese threat model): su posesión es la autorización para ESE carrito, de bajo
  impacto (nunca protegió dinero hasta ahora; con este ticket empieza a proteger la composición de
  una compra, ver R-01 abajo).
- **Api → Application**: `[Authorize(Roles = "Comprador")]` es la única puerta — un JWT válido con
  rol `Organizador` (o sin rol, tokens emitidos antes de este ticket si alguno sobreviviera más allá
  de su expiración de 60 minutos) es rechazado con 403 antes de llegar a `CompraService`.
- **Application → Infrastructure (Redis ↔ SQL)**: sin transacción distribuida real entre ambos
  almacenes — la garantía de integridad depende enteramente del ORDEN de las operaciones (CHECK →
  COMMIT → RELEASE), no de un mecanismo de dos fases. Esto es un límite explícito del diseño, no un
  descuido: se documenta y se mitiga con el orden mandatado en la spec (ver R-02).

## Risks

🔴 **CRITICAL: ninguno.**

🟠 **HIGH: ninguno.**

🟡 **MEDIUM**

- **R-01 (Spoofing/Elevation of Privilege — primer uso real de roles de Identity):** si el seeding
  de roles fallara silenciosamente, o si `[Authorize(Roles = "Comprador")]` no filtrara realmente
  por rol (ej. por una configuración de `RoleClaimType` incorrecta), un organizador autenticado
  podría confirmar compras como si fuera comprador, o el gate podría no filtrar nada. **Mitigación:**
  el rol viaja como claim `role` dentro del JWT firmado (mismo mecanismo de firma ya auditado), no
  hay forma de que el cliente lo modifique sin invalidar la firma; `JwtSecurityTokenHandler` mapea
  el claim corto `"role"` a `ClaimTypes.Role` por default (`DefaultInboundClaimTypeMap`), sin
  configuración adicional que pueda desalinearse silenciosamente. **Verificación obligatoria en
  CODE/SAST:** un test de integración real confirma explícitamente que un JWT con rol `Organizador`
  recibe 403 en `POST /api/compras/confirmar` (ya en la spec, Block 3) — no basta con "debería
  funcionar", el gate se prueba con una request HTTP real contra el rol equivocado.
- **R-02 (Tampering/Integrity — coordinación sin transacción distribuida entre Redis y SQL):** si el
  orden CHECK→COMMIT→RELEASE se invirtiera o se colapsara (ej. liberar Redis antes de confirmar que
  SQL commiteó), una falla de SQL a mitad de camino podría liberar cartones reservados sin que
  exista ninguna compra real — la reserva desaparece pero nadie la compró, y otro participante
  podría tomarla sin que el primero haya recibido confirmación ni error claro. **Mitigación:** el
  orden es una decisión cerrada en PLAN, no una sugerencia — `LiberarCarritoConfirmadoAsync` solo se
  invoca DESPUÉS de que `CrearVariasAsync` (SQL) retorna exitosamente, verificado explícitamente con
  un test de secuencia (`CompraServiceTests`, Block 2: "invoca `LiberarCarritoConfirmadoAsync`
  DESPUÉS de `CrearVariasAsync`" + "si `CrearVariasAsync` falla, `LiberarCarritoConfirmadoAsync`
  NUNCA se invoca"). Si el paso SQL falla, Redis queda intacto — el TTL de 5 minutos ya existente
  (FEAT-008b) es la red de seguridad: en el peor caso el cartón queda reservado unos minutos más,
  nunca "confirmado sin compra" ni "vendido dos veces".
- **R-03 (CUIT compartido entre roles, potencial denegación de registro cruzada):** organizador y
  comprador comparten el mismo índice único de `Cuit` en `AspNetUsers` — una persona que ya se
  registró como organizador con un CUIT no puede registrarse como comprador con el mismo CUIT bajo
  una cuenta separada, y viceversa. **Mitigación: accepted risk**, aceptado por: el equipo del
  proyecto (decisión de PLAN, no un usuario final). Justificación: el PRD de este ticket lo declara
  explícitamente fuera de alcance ("no resuelto en este ticket"); el CUIT es un identificador fiscal
  único por persona/entidad en la vida real, así que la colisión es infrecuente y el impacto es "no
  puede completar un registro", no una fuga de datos ni un bypass de control. **Revisar cuando:** si
  el producto necesita que una misma persona sea organizador y comprador con cuentas separadas, o si
  se reciben reportes reales de este conflicto.

🟢 **LOW**

- **R-04 (Information Disclosure — enumeración de mail en registro de comprador):** `POST
  /api/compradores/registro` devuelve 409 explícito si el mail ya existe — mismo patrón ya aceptado
  para organizador desde FEAT-001a (no es un riesgo nuevo introducido por este ticket, es
  consistencia con una decisión de threat modeling ya tomada).
- **R-05 (Tampering — subqueries `NOT EXISTS` nuevas en `DescubrimientoRepository`/
  `ObtenerParaCarritoAsync`):** ambas queries suman `AND NOT EXISTS (SELECT 1 FROM CompraCartones cc
  WHERE cc.CartonId = c.Id)` a SQL crudo (`FromSqlRaw`, ya auditado en FEAT-008b) y LINQ
  respectivamente. **Evaluado como MÁS seguro que el caso ya auditado, no un riesgo nuevo:** a
  diferencia de la cláusula `NOT IN` de FEAT-008b (que interpola `Guid.ToString()` de una colección
  variable), esta subquery es texto **completamente fijo**, sin ningún valor interpolado ni
  concatenado — no hay ninguna superficie de inyección que auditar porque no hay ningún dato
  variable en el string. SAST (CODE) debe confirmar que efectivamente no se agregó ningún parámetro
  dinámico a esta cláusula específica.

## Sensitive data classification (F-TM-05)

`Comprador`/`ApplicationUser` (rol Comprador): Apellido, Nombre, CUIT, Mail — mismo nivel de
sensibilidad y misma protección ya exigida para organizador (RNF-04/RNF-09 del PRD maestro): acceso
restringido por rol, nunca en logs. `Compra`: `OrganizadorId`, `CompradorId`, `MedioPago`, montos —
dato de negocio, no PII directa, pero vinculado a PII vía las FK — mismo criterio de acceso
restringido (un comprador nunca puede leer compras de otro comprador; verificación de ese control de
acceso específico es una capacidad que este ticket no expone todavía como endpoint de lectura — se
declara explícitamente fuera de alcance, ticket FEAT-009d es quien primero expone "mis compras/mis
cartones" al comprador).

## Mitigations folded into the spec

1. `compradorId` derivado exclusivamente del claim JWT, nunca de un parámetro de request — ya en la
   spec, Block 3.
2. Test HTTP real de rechazo por rol incorrecto (`Organizador` → 403 en el endpoint de confirmación)
   — ya en la spec, Block 3.
3. Orden CHECK→COMMIT→RELEASE mandado explícitamente, con test de secuencia — ya en la spec, Block 2.
4. `CartonId` `UNIQUE` en `CompraCartones` como defensa en profundidad final contra doble venta,
   independiente de la revalidación de Redis — ya en la spec, Block 1.
5. Rate limiting nuevo (`"compradores"` 5/min/IP, `"compras"` 10/5min por comprador autenticado) —
   ya en la spec, Block 3.
6. Subqueries `NOT EXISTS` nuevas sin ningún valor interpolado — a confirmar explícitamente en SAST
   (CODE) que no se introduce ningún parámetro dinámico en esa cláusula específica.

Ningún riesgo CRITICAL/HIGH identificado. R-01/R-02 (MEDIUM) quedan mitigados por diseño con
verificación obligatoria vía tests de integración/secuencia, no solo por argumento. R-03 (MEDIUM)
queda como accepted risk explícito, con las tres condiciones requeridas (quién lo acepta, por qué,
cuándo se revisa) documentadas arriba.

---

**Risks: C:0 H:0 M:3 (mitigados/aceptados) L:2 (accepted)**
**Veredicto: PASSED**
