# Contributing to Filekin

Thank you for helping build Filekin.

## Before changing code

1. Read `AGENTS.md`, `PROJECT-SETUP.md`, `HANDOFF.md`, and the six master specification documents listed in `README.md`.
2. Treat confirmed decisions as requirements. If evidence conflicts with a specification, document the conflict in `HANDOFF.md` and request a product decision before building around it.
3. Keep the disposable project under `spikes/` isolated. Reimplement validated concepts behind the production boundaries instead of copying the spike wholesale.

## Development setup

Install the .NET 10 SDK on Windows, then run:

```powershell
dotnet restore Filekin.sln
dotnet build Filekin.sln --configuration Release --no-restore
dotnet test Filekin.sln --configuration Release --no-build
dotnet format Filekin.sln --verify-no-changes --no-restore
```

## Code conventions

- Follow `.editorconfig`; use `dotnet format` before submitting a change.
- Keep nullable reference types and implicit usings enabled.
- Treat compiler and analyzer warnings as errors.
- Put platform-neutral domain/application logic in `Filekin.Core`.
- Isolate Windows, PowerShell, WPF, and ConPTY dependencies from the core project.
- Add focused tests for behavior changes. Use integration tests for boundaries whose behavior depends on Windows or external processes.
- Prefer clear, direct implementations over speculative abstractions.
- Do not use stock WPF templates as Filekin's product design.

## Change scope

Keep changes aligned with the confirmed v1 scope. Do not add new product behavior, compatibility promises, dependencies, or architectural patterns without a documented need and, where required, an owner decision.

Before ending meaningful work, update `HANDOFF.md` so the next agent can continue without the chat history: the current phase, the exact next task, anything newly blocked and why, any standing contract or trap the work established, and any new known problem.

Keep it short. Do **not** append a per-session changelog, a list of changed files, or a test count — git records all three and none of them help the next agent decide anything. When a feature is finished, replace its entry with the one-paragraph conclusion a future agent needs and move any long record to `HANDOFF-ARCHIVE.md`. `HANDOFF.md` should stay under about 500 lines.

## Pull requests

Describe the user-visible or engineering outcome, the specification/decision it implements, and the validation performed. Keep unrelated cleanup separate so reviewers can evaluate behavior and risk clearly.
