# Verify Report — FEAT-009d ("Mis cartones" del comprador y actualización de datos de cuenta)

| Field | Value |
|-------|-------|
| Ticket | FEAT-009d |
| Date | 2026-08-25 |
| Verifier | daw-module-verifier (agente independiente, no escribió el código) |
| Alcance | Verificación de FASE completa del ticket (6 bloques) contra PRD + spec, no repetición de las revisiones por bloque ya hechas en CODE |
| Veredicto | **PASSED** |

## Trazabilidad PRD → Código → Tests (16/16 AC)

| AC | Resultado |
|---|---|
| AC-01 (listado con 7 campos) | ✅ PASS — `MisCartonesController.Listar` / `MisCartonesService.ListarAsync` / `CompraRepository.ListarCartonesDelCompradorAsync` → `MisCartonesControllerTests.Listar_ConCartonesAdquiridos_DevuelveTodosLosCamposEsperados`, verifica los 7 campos con datos reales |
| AC-02 (compras en cualquier estado, incluidas canceladas) | ✅ PASS — `CompraRepository.ListarCartonesDelCompradorAsync` sin filtro de estado → `Listar_ConComprasEnDistintosEstados_LasIncluyeTodasConSuEstado`, siembra Pendiente/Confirmado/Cancelado |
| AC-03 (pageSize clampeado a 50) | ✅ PASS — `MisCartonesService.ListarAsync` (`Math.Min(pageSize, 50)`) → `ListarAsync_ConPageSizeMayorA50_ClampeaA50YDevuelveElTotalSinPaginar` |
| AC-04 (PDF válido, Content-Type correcto) | ✅ PASS — `MisCartonesController.DescargarPdf` / `QuestPdfCartonRenderer.Renderizar` → `DescargarPdf_ConCartonPropio_Devuelve200ConContentTypePdf` (magic bytes `%PDF`) + `QuestPdfCartonRendererTests.Renderizar_IncluyeElNumeroCorrelativoEnElDocumento` |
| AC-05 (anti-enumeración, cartón ajeno/inexistente) | ✅ PASS — `CartonNoEncontradoException` (mensaje estático) → `DescargarPdf_ConCartonAjeno_Devuelve404` + `DescargarPdf_ConCartonInexistente_Devuelve404ConElMismoCuerpo`, cuerpos comparados byte a byte |
| AC-06 (actualización refleja el estado en la respuesta) | ✅ PASS — `CompradorService.ActualizarCuentaAsync` → `ActualizarCuentaAsync_ConTodosLosSorteosLejanos_PersisteLosCambios` + `CompradoresControllerTests.ActualizarCuenta_ConDatosValidos_Devuelve200ConElEstadoActualizado` |
| AC-07 (bloqueo dentro de 60 min) | ✅ PASS — `CompradorCuentaRepository.TieneSorteoInminenteAsync` → `ActualizarCuentaAsync_ConSorteoDentroDe60Minutos_LanzaPlazoModificacionVencidoException` (confirma que `ActualizarDatosAsync` nunca se invoca) + controller test 409 |
| AC-08 (colisión de mail) | ✅ PASS — `MailEnUsoException`, chequeo antes de escribir → `...LanzaMailEnUsoExceptionSinModificarNada` (`Times.Never()`) + controller test 409 |
| AC-09 (CUIT: longitud y dígito verificador distinguibles) | ✅ PASS — `CuitValidator`, evaluados por separado → mensajes verificablemente distintos ("11 dígitos" vs. "dígito verificador") |
| AC-10 (listado no depende del outbox de mail) | ✅ PASS — `Listar_ConEnvioDeMailFallido_DevuelveElListadoCompleto`, siembra un envío fallido y confirma que el cartón igual aparece |
| AC-11 (colisión de CUIT) | ✅ PASS — `CuitEnUsoException` → `...LanzaCuitEnUsoExceptionSinModificarNada` + controller test 409 |
| AC-12 (correlativo 1..N único por bingo) | ✅ PASS — `BingoService.CrearAsync` → `BingoServiceTests.CrearBingo_AsignaCorrelativosDistintosDesde1HastaN` |
| AC-13 (correlativo nunca acepta como input) | ✅ PASS — constraint `:guid` en los 8 endpoints con id de cartón → `Agregar_ConElNumeroCorrelativoEnLugarDelGuidDelCarton_Devuelve404...` + `DescargarPdf_ConNumeroCorrelativoEnLugarDelGuid_NoDevuelveCarton` |
| AC-14 (mismo correlativo en las 4+ superficies) | ✅ PASS — `Listar_ElCorrelativoCoincideConElDeLasOtrasSuperficies`, end-to-end real sobre descubrimiento, carrito, confirmación, mail y mis cartones (5 superficies, supera lo exigido) |
| AC-15 (descubrimiento ignora el correlativo como filtro) | ✅ PASS — `PorOrganizador_ConElCorrelativoComoCriterioDeBusqueda_LoIgnoraYDevuelveCadaCartonConSuCorrelativo` |
| AC-16 (orden: contraseña antes que enumeración) | ✅ PASS — `ActualizarCuentaAsync_ConContrasenaIncorrectaYMailYaEnUso_LanzaContrasenaIncorrectaException`, confirma el tipo lanzado y que `ExisteMailDeOtraCuentaAsync` nunca se invoca |

**16/16 PASS, ninguno superficial** — todos verifican cuerpo/datos reales o interacciones (`Times.Never`/`Times.Once`), no solo status code.

## Spec — tareas por bloque

- ✅ Block 1 (correlativo: modelo, generación, migración): 6/6 archivos y tests, migración con backfill vía `ROW_NUMBER() OVER (PARTITION BY BingoId ORDER BY Id)`.
- ✅ Block 2 (propagación a proyecciones y PDF): 11/11 archivos de producción, `ICartonPdfRenderer` extendido, ninguna ruta de entrada acepta el correlativo. Incluye ADR-003 (CartonCompradoResponse/CompraCreada.Cartones, corrección documental del spec).
- ✅ Block 3 (mis cartones: listado paginado): `MisCartonesController` standalone (D-03 respetado), join tipado de 4 tablas.
- ✅ Block 4 (PDF on-demand): `CartonNoEncontradoException` (patrón anti-enumeración de FEAT-009c), PDF nunca persistido.
- ✅ Block 5 (dominio/Identity/regla 60 min): `TimeProvider` inyectado (cero `DateTime.UtcNow`), `ICompradorCuentaRepository` puerto propio (D-03), orden estricto de verificaciones.
- ✅ Block 6 (endpoint/cookie/middleware): `PUT /mi-cuenta`, reemisión de cookie con los mismos flags del login, política `"comprador-cuenta"` reutilizada de Block 3, G-13 (cleanup por `Id`) corregido.
- ✅ Fix de cierre de CODE (F-SAST-10, HIGH): `CompradorService.cs` ya no interpola errores crudos de Identity en el mensaje de excepción que cae en el catch genérico del middleware — evita fuga del mail a logs del servidor.

**6/6 bloques implementados íntegramente, sin gaps.**

## Cobertura (medida con `dotnet test --collect:"XPlat Code Coverage"` + `reportgenerator`, no estimación)

El agregado de la solución completa no es la métrica correcta (incluye código de tickets anteriores no tocado). Sobre las clases nuevas/modificadas por este ticket:

| Clase | Cobertura |
|---|---|
| `MisCartonesController`, `CompradoresController`, `CarritoController`, `CartonesController`, `ComprasController` | 100% |
| `MisCartonesService`, `CartonesAdquiridosPaginados`, DTOs nuevos | 100% |
| `Carton` (Domain), las 5 excepciones nuevas de dominio | 100% |
| `CompraRepository`, `CompradorCuentaRepository`, `BingoRepository`, `DescubrimientoRepository`, `QuestPdfCartonRenderer`, `AppDbContext`, migración nueva | 100% |
| `ExceptionHandlingMiddleware` | 95% |
| `CarritoService` | 96.2% |
| `CompradorService` | 94% (101/107 líneas) |
| `IdentityGateway` | 92% (98/106 líneas; líneas sin cubrir son ramas de `Organizadores` no tocadas por este ticket) |

Todas ≥92%, por encima del piso de 80% y del recomendado 90% para lógica de negocio.

## Sad paths

- 3 endpoints nuevos (`GET mis-cartones`, `GET mis-cartones/{id}/pdf`, `PUT mi-cuenta`) y el método de dominio `CompradorService.ActualizarCuentaAsync` — todos con sad paths de validación, autorización, autenticación y rate limiting confirmados con tests.

## Calidad

- ✅ `dotnet build BingoCart.sln`: 0 warnings, 0 errores.
- ✅ `dotnet format BingoCart.sln --verify-no-changes`: limpio.
- ✅ `dotnet build -warnaserror:CS8019,IDE0005`: 0 warnings, 0 errores (sin imports sin usar ni código muerto detectable).

## Tests listados explícitamente en el spec (F-VER-06)

Conteo exacto por bloque, confirmado archivo por archivo, no asumido por continuidad:

| Bloque | Requeridos | Encontrados |
|---|---|---|
| Block 1 | 6 | 6/6 |
| Block 2 | 8 | 8/8 |
| Block 3 | 12 | 12/12 |
| Block 4 | 9 | 9/9 |
| Block 5 | 17 | 17/17 |
| Block 6 | 15 | 15/15 |
| **Total** | **67** | **67/67**, todos en verde |

## Suite completa

- ✅ 376/376 tests no-E2E en verde (60 Domain + 92 Application + 98 Infrastructure + 126 Api), confirmado en 2 corridas consecutivas — sin residuos del fix de G-13.
- ⚠️ `BingoCart.E2E.Tests`: 0/3, `ERR_CONNECTION_REFUSED` a `localhost:8000`/`8080` — servidores no levantados en este entorno, tests de registro/login de organizador (FEAT-001a), ningún archivo de este ticket los toca. Fallo ambiental preexistente, no relacionado con FEAT-009d.

## Warnings (no bloqueantes)

1. Los tests de Infrastructure/Api contra SQL Server real dependen de que el contenedor Docker esté levantado, sin fallback/skip documentado — patrón ya establecido en tickets anteriores, no una regresión de este ticket.
2. `IdentityGateway` (92.4%) y `CompradorService` (94.3%) apenas por debajo del ideal de cobertura completa de lógica de negocio, aunque muy por encima del piso de 80%/90%. Las líneas sin cubrir de `IdentityGateway` son ramas de `Organizadores` no tocadas por este ticket.

## FAILs (bloqueantes)

Ninguno.

---

**Total: 16/16 AC PASS | Spec: 6/6 bloques completos | 67/67 tests del spec en verde | 0 FAIL | 2 WARN (no bloqueantes)**
**Resultado: PASSED. Listo para RELEASE.**
