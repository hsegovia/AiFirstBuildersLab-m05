# Spec FEAT-002: Reordenar directorios backend bajo backend/

| Field | Value |
|-------|-------|
| Ticket | FEAT-002 |
| PRD | docs/daw/prd/prd-FEAT-002.md |
| Tier | FEATURE |
| Date | 2026-08-16 |
| Spec loops | 0 |

## Summary

Mueve el backend .NET completo (`src/`, `tests/`, `BingoCart.sln`) a `backend/`, sin subnivel `src/`
intermedio (ADR-002 enmendado durante esta misma fase PLAN). Es un movimiento de archivos puro —
ningún namespace, clase ni comportamiento cambia — pero exige actualizar cada referencia de ruta
(`.sln`, `ProjectReference`, `docker-compose.yml`, `Dockerfile`, `.dockerignore`) y re-verificar
build, suite completa y el stack contenedorizado desde cero para garantizar cero regresión.

## Coverage: PRD → blocks

| Requirement | Covered by |
|---|---|
| FR-01 | Block 1 |
| FR-02 | Block 1 |
| FR-03 | Block 1 |
| FR-04 | Block 2 |
| FR-05 | Block 2 |
| FR-06 | Block 2 |
| FR-07 | Block 1 (confirmado que no aplica cambio — `.gitignore` ya usa patrones recursivos `**/bin/`/`**/obj/`) |
| NFR-01 | Strategy: Block 1 exige que los 43 tests existentes pasen exactos, sin renombrar ni alterar ninguno — cualquier diferencia es visible en el conteo. |
| NFR-02 | Strategy: Block 2 exige un ciclo `docker-compose down -v && up --build` completo desde cero (no reutilizando contenedores existentes) antes de dar el bloque por cerrado. |

## Dependencies between blocks

Block 2 depende de Block 1 (el `Dockerfile` movido y las rutas de `docker-compose.yml` dependen de
que los archivos ya estén en `backend/`). Orden: 1 → 2, sin paralelismo posible.

**Decisiones cerradas en PLAN (no reabrir en CODE):**
- Estructura final: `backend/BingoCart.Api/`, `backend/BingoCart.Application/`,
  `backend/BingoCart.Domain/`, `backend/BingoCart.Infrastructure/` (sin `backend/src/` — ADR-002
  enmendado), `backend/tests/<5 proyectos>/`, `backend/BingoCart.sln`.
- La carpeta virtual de Visual Studio `"src"` en el `.sln` se elimina (ya no corresponde a ninguna
  carpeta física).
- `backend/.dockerignore` espeja `frontend/.dockerignore` completo (`bin/`, `obj/`, `.git/`,
  `*.env`), no solo `bin/`/`obj/` — **y además preserva `appsettings.*.local.json`**, patrón que
  el `.dockerignore` de raíz actual ya tiene y que `frontend/.dockerignore` no necesita (Angular no
  tiene ese mecanismo de override local). Sin este patrón, un `appsettings.{Environment}.local.json`
  creado localmente con secretos reales (connection string, JWT signing key) quedaría embebido en
  una capa de la imagen Docker (hallazgo de `/daw-threat-modeling`, riesgo HIGH de Information
  Disclosure — ver `docs/daw/security/threat-FEAT-002.md`).
- `.dockerignore` de raíz se elimina — tras este cambio ningún servicio de `docker-compose` usa
  `context: .`.

## Block 1 — Mover proyectos .NET y .sln a backend/

**Files**
- `src/BingoCart.Api/` → `backend/BingoCart.Api/` (moved, vía `git mv`)
- `src/BingoCart.Application/` → `backend/BingoCart.Application/` (moved, vía `git mv`)
- `src/BingoCart.Domain/` → `backend/BingoCart.Domain/` (moved, vía `git mv`)
- `src/BingoCart.Infrastructure/` → `backend/BingoCart.Infrastructure/` (moved, vía `git mv`)
- `tests/` → `backend/tests/` (moved completo, vía `git mv`, los 5 proyectos de test)
- `BingoCart.sln` → `backend/BingoCart.sln` (moved, vía `git mv`)
- `backend/BingoCart.sln` (modified tras el move) — actualiza cada entrada `Project(...)`:
  `"src\BingoCart.X\BingoCart.X.csproj"` → `"BingoCart.X\BingoCart.X.csproj"` y
  `"tests\BingoCart.X.Tests\BingoCart.X.Tests.csproj"` se mantiene igual (el segmento `tests\` sigue
  siendo un subnivel real dentro de `backend/`). Elimina la entrada de solution folder virtual
  `"src"` y su bloque `NestedProjects` asociado.
- Los 5 `.csproj` de test bajo `backend/tests/BingoCart.*.Tests/` (modified) — cada
  `ProjectReference` que hoy dice `..\..\src\BingoCart.X\BingoCart.X.csproj` pasa a
  `..\..\BingoCart.X\BingoCart.X.csproj` (se elimina solo el segmento `src\`; la profundidad `..\..\`
  no cambia porque `backend/tests/BingoCart.X.Tests/` tiene la misma anidación relativa a `backend/`
  que `tests/BingoCart.X.Tests/` tenía relativa a la raíz).
- Los 4 `.csproj` de producción bajo `backend/BingoCart.*/` — revisar cada uno; si alguna
  `ProjectReference` cruzada entre proyectos de producción incluye el segmento `src\` explícito,
  corregirla de la misma forma. (Análisis previo: como ya eran siblings dentro de `src/`, es
  esperable que usen `..\BingoCart.X\...` sin `src\` y no requieran cambio — confirmarlo leyendo
  cada uno antes de descartar el ajuste, no asumir.)
- `backend/tests/BingoCart.E2E.Tests/RegistroOrganizadorE2ETests.cs` (modified) — actualiza el
  comentario XML que referencia la ruta vieja `src/BingoCart.Api/appsettings.Development.json` a la
  ruta nueva `backend/BingoCart.Api/appsettings.Development.json`.

**Logic**
Movimiento de archivos puro (preservando historial de `git` vía `git mv`, no delete+create) más
ajuste mecánico de rutas relativas en `.sln` y `.csproj`. Ningún namespace de C# cambia (confirmado
en PLAN por `daw-arch-auditor`: los namespaces están declarados explícitamente en el código, no se
infieren de la ruta del archivo).

**Input validation**
N/A — bloque de infraestructura de repositorio, sin input de usuario.

**Error handling**
N/A — sin lógica de negocio en este bloque.

**Required tests**
- [ ] `dotnet build backend/BingoCart.sln` — 0 errores
- [ ] `dotnet test backend/BingoCart.sln` — 43/43 tests, exactamente los mismos nombres y
  aserciones que existían antes del movimiento (ningún test se agrega, renombra ni elimina)

**Completion criterion**
Build y suite completa en verde sobre la nueva ubicación, sin ningún cambio de comportamiento
(mismos 43 tests con las mismas aserciones que antes del movimiento).

## Block 2 — Actualizar Docker/config y verificación end-to-end completa

**Files**
- `docker-compose.yml` (modified) — servicio `api`: `build.context` pasa de `.` a `./backend`,
  `build.dockerfile` pasa de `src/BingoCart.Api/Dockerfile` a `BingoCart.Api/Dockerfile` (relativo
  al nuevo contexto).
- `backend/BingoCart.Api/Dockerfile` (modified, ya movido en Block 1) — los `COPY` internos que hoy
  referencian `src/BingoCart.X/...` relativo a la raíz del repo pasan a referenciar
  `BingoCart.X/...` relativo al nuevo contexto `backend/`.
- `backend/.dockerignore` (new) — espejo de propósito (no de sintaxis literal) de
  `frontend/.dockerignore`: `**/bin/`, `**/obj/` (con prefijo `**/`, NO planos — verificado en
  Block 2 que `bin/`/`obj/` sin `**/` solo excluyen el nivel superior del contexto y no los
  `bin/`/`obj/` anidados en cada uno de los 4 proyectos .NET, lo que hacía fallar
  `dotnet publish` dentro del build de Docker; `frontend/.dockerignore` puede usar rutas planas
  porque Angular no anida `bin/`/`obj/` por proyecto), `.git/`, `*.env`, MÁS
  `appsettings.*.local.json` (mitigación del threat model, preserva el hardening que tenía el
  `.dockerignore` de raíz frente a overrides locales con secretos reales).
- `.dockerignore` (deleted, raíz) — sin consumidor tras el cambio (ningún servicio usa ya
  `context: .`).
- Nota operativa (no es un archivo del repositorio, no se commitea): `.claude/settings.local.json`
  (no trackeado por git) tiene permisos Bash con rutas hardcodeadas al esquema viejo — actualizar
  para evitar fricción de permisos en el resto de la sesión, sin que esto forme parte del diff del
  ticket.

**Logic**
Redirige el build de la Api al nuevo contexto simétrico con el del frontend, y cierra el ciclo de
verificación completo del ADR-002 (los 6 puntos de su checklist original, adaptados a la estructura
enmendada).

**Input validation**
N/A.

**Error handling**
N/A — configuración de build/despliegue, no código de aplicación.

**Required tests**
- [ ] `docker-compose down -v && docker-compose up --build -d` (desde cero, sin reutilizar
  contenedores) → los 3 servicios (`db`, `api`, `web`) quedan `Up`/healthy
- [ ] `curl -i http://localhost:8080/swagger/index.html` → 200
- [ ] `curl -i http://localhost:8000` → 200
- [ ] `POST /api/organizadores/registro` con un mail único real contra el stack contenedorizado →
  201 (AC-04, cero regresión funcional en el flujo end-to-end); limpiar el dato de prueba después
- [ ] Grep recursivo sobre el repo trackeado (excluyendo `docs/daw/`, registro histórico) buscando
  `src/BingoCart`, `src\\BingoCart`, `tests/BingoCart` (fuera de `backend/tests/`), y `BingoCart.sln`
  en la raíz → 0 resultados (AC-05, red de seguridad contra referencias rotas silenciosas)

**Completion criterion**
Los 3 servicios funcionan end-to-end desde un stack recreado de cero, el flujo de registro responde
201 real contra los contenedores, y el grep de rutas viejas no encuentra ninguna referencia activa
fuera de documentación histórica.

## Final verification

`dotnet build`/`dotnet test` sobre `backend/BingoCart.sln` en verde (43/43). `docker-compose up
--build` desde cero levanta los 3 servicios y el flujo de registro (AC-04) responde 201 real. Grep
de rutas viejas (AC-05) sin resultados fuera de `docs/daw/`. Ningún archivo de código o configuración
trackeado referencia el esquema `src/`/`tests/`/`BingoCart.sln` de raíz.
