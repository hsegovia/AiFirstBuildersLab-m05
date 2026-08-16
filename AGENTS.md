# AGENTS.md — project context

> **DAW template.** Fill in the `[...]` with what is true of YOUR project and delete what does not
> apply. This file describes **the project**; **the process** is DAW's job (phases, gates, when to
> test, when to commit). Do not mix the two: process rules written here compete with the pipeline's.
>
> It is **tool-agnostic on purpose**: Claude Code reads it through the import in `CLAUDE.md`, Codex
> CLI, Copilot CLI, Cursor and OpenCode read it directly, and Gemini CLI gets it through
> `GEMINI.md`. The same file serves whichever tool you open the repo with — which is the point:
> porting the pipeline to another tool must not mean rewriting what your project is.

---

## Language

**Always respond in the language the user writes in.** Write every artifact you produce — PRDs,
specs, ADRs, reports, commit messages, status lines — in that same language, regardless of the
language these instructions are written in.

If this project has a fixed working language, state it here and use it instead:

> Working language: `[e.g. Spanish — write all artifacts in Spanish]`

---

## What this project is

Plataforma web donde organizadores publican bingos con cartones numerados y participantes los descubren, seleccionan y compran online, con reserva temporal de carrito y control de stock que evita vender el mismo cartón dos veces.

**Reference PRD:** `docs/daw/prd/[your-prd].md`

---

## Stack

**This is the only place the stack lives.** DAW reads it from here and generates no derived file.
Fill it in even if the repo is empty: without a stack there is nothing to plan or implement against.

If the repo already has code and this section is empty, DAW will detect the stack from your config
files and **propose the text for you to paste here**. You always confirm it.

| Field | Value |
|-------|-------|
| Language FrontEnd | TypeScript 5.x |
| Language BackEnd | C# 12 |
| Runtime FrontEnd | Node 20 (Angular CLI) |
| Runtime BackEnd | .NET 8 |
| Framework FrontEnd | Angular 18 (NgModules, Angular Material 18 / MD3, Tailwind CSS) — puerto 8000 |
| Framework BackEnd | ASP.NET Core 8 Web API (Swagger) — puerto 8080 |
| Database | SQL Server 2022 (Docker Compose, puerto 14330) + Entity Framework Core |
| Cache | Redis (reserva de carrito) |
| Auth | ASP.NET Core Identity + JWT |
| Email / PDF | MailKit (SMTP) + QuestPDF (cartones) |
| Test runner | Playwright para .NET (C#/xUnit) — E2E |
| Linter / formatter | ESLint + Prettier (FrontEnd) · dotnet-format (BackEnd) |
| Package manager | npm (FrontEnd) · NuGet (BackEnd) |

---

## Architecture conventions

**DAW validates your code against this section** during the CODE phase, via `daw-validate-arch`.
Leave it empty and that validation has nothing to compare against, so it stops being worth running.

- **Folder structure:**
  - FrontEnd: `src/app/features/<feature>/` con subcarpetas `components/`, `services/`, `models/` dentro de cada `NgModule` (ej. `features/cart/`, `features/cards/`).
  - BackEnd: por capas a nivel solución — `Api/` (controllers), `Application/` (servicios, lógica de negocio), `Domain/` (entidades), `Infrastructure/` (EF Core, Redis, MailKit, QuestPDF).
- **Layer separation:** el FrontEnd nunca llama directamente a la API sin pasar por un service Angular dedicado por recurso; el BackEnd nunca expone entidades de EF Core en la API (siempre DTOs) y nunca pone lógica de negocio en controllers ni en la capa de datos.
- **Error handling:** BackEnd con excepciones tipadas por dominio (ej. `CartNotFoundException`) capturadas por un middleware global que las traduce a respuestas HTTP consistentes — nunca un catch silencioso. FrontEnd con manejo centralizado de errores HTTP vía interceptor, nunca un `.subscribe()` sin `error` handler.
- **Naming:** archivos Angular en kebab-case (`cart-summary.component.ts`), componentes/clases en PascalCase (`CartSummaryComponent`); en .NET, clases y archivos en PascalCase (`CartService.cs`), variables locales en camelCase.
- **Dependencies:** no se agregan librerías nuevas (npm o NuGet) sin justificarlas en el spec/PRD — coherente con el principio ya definido de "no features outside the PRD".

---

## Code conventions

- **TypeScript (Angular):** sin `any`. Si es inevitable (ej. tipado de una librería de terceros sin `.d.ts`), va con comentario explicando por qué. Preferir `unknown` + type guard antes que `any`.
- **C# (.NET):** sin `dynamic` ni `object` genérico donde haya un tipo conocido; usar tipos explícitos o genéricos fuertemente tipados. Nullable reference types habilitado (`<Nullable>enable</Nullable>`) — no silenciar warnings de nulabilidad con `!` sin justificar.
- **Funciones puras / efectos en los bordes:** en Angular, la lógica de transformación de datos (pipes, mappers) debe ser pura; las llamadas HTTP y el estado mutable viven solo en services. En .NET, los métodos de dominio (`Domain/`) no deben tener side effects (I/O, DB, red); esos efectos quedan en `Infrastructure/` y `Application/`.
- **Comentarios solo cuando el *por qué* no es obvio:** no comentar qué hace una línea si el código ya lo dice (ej. no `// suma el total` sobre un `total += price`); sí comentar decisiones no evidentes (ej. por qué se usa una excepción documentada como el upgrade de Angular 16→18, o por qué un campo se cachea en Redis con un TTL específico).
- **Async/await consistente:** en .NET, todo I/O (EF Core, Redis, MailKit) usa `async`/`await` de punta a punta — nunca `.Result` ni `.Wait()` que puedan bloquear el hilo. En Angular, preferir `async` pipe en templates antes que `subscribe()` manual cuando sea posible.
- **Inmutabilidad por defecto:** en TypeScript, preferir `readonly` en propiedades de modelos/DTOs que no deban mutarse tras su creación. En C#, preferir `record` para DTOs y modelos de solo lectura en vez de `class` mutable.

---

## What NOT to do in this project

This section is worth its weight in gold: it is where the scars go, the things that already went
wrong once.

- No superar 5.000 cartones por bingo (RF-03).
- No generar números de cartón sin CSPRNG ni exponer IDs secuenciales predecibles (RNF-07 / R-02).
- No implementar procesamiento automático de pagos: la conciliación de Efectivo/Transferencia es siempre manual del organizador (Fuera de Alcance).
- No guaardens reglas de negocio en la base de datos. 

---

## Domain glossary

The terms specific to your product, so the agent uses them correctly instead of inventing synonyms.

<!-- - **[Term]:** [what it means exactly, here]
- **[Term]:** [what it means exactly, here] -->

---

> ℹ️ **What does NOT belong in this file, because DAW provides it:** the order work happens in, when
> the spec gets written, when tests run, when to commit, what it takes to move between phases. All
> of that lives in `.daw/` and applies on its own.

<!-- BEGIN DAW (managed by DAW — do not edit by hand) -->
# DAW — Dilux Agentic Workflow

This repo uses **DAW**: an agent-driven development pipeline with the phases
`CLASSIFY → DEFINE → PLAN → CODE → VERIFY → RELEASE`.

Before answering, read `.daw/orchestrator.md` and run its Boot Sequence. It is a strict state
machine: it decides what you are allowed to do based on the phase recorded in `.daw-state.json`.

The project's own context — stack, architecture, domain — is elsewhere in this file. It lives here,
in `AGENTS.md`, and not in any one tool's file, on purpose: it is tool-agnostic and comes along
unchanged when the pipeline is ported to another agent.
<!-- END DAW -->
