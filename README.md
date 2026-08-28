# Filekin

[![CI](https://github.com/mefisme/filekin/actions/workflows/ci.yml/badge.svg)](https://github.com/mefisme/filekin/actions/workflows/ci.yml)

Filekin is a keyboard-first Windows file manager + terminal.

The required PowerShell runspace + ConPTY technical spike is complete and documented, and production implementation is under way. The application currently provides the Files workspace and its command bar, ConPTY-backed terminal tabs, saved Locations, the Places, Drives, Recycle Bin, and Settings surfaces, and the app-owned file, archive, inspection, and launch commands. Confirmed v1 scope is not yet complete — Files Back/Forward, the file context menu, `/find`, and durable `/history` + `/undo` are still outstanding. `HANDOFF.md` tracks the current state.

## Source of truth

Read these documents before changing product behavior or architecture:

- `AGENTS.md`
- `PRODUCT.md`
- `FEATURES.md`
- `UX-DESIGN.md`
- `ARCHITECTURE.md`
- `ENGINEERING-GUARDRAILS.md`
- `DECISIONS.md`
- `HANDOFF.md`

`HANDOFF-ARCHIVE.md` holds frozen handoff history and is not current instruction.

`PROJECT-SETUP.md` records the required setup sequence. The disposable validation project under `spikes/` is intentionally outside the production solution and must not become the production application.

## Production structure

```text
src/
├── Filekin.App/                    WPF composition and presentation
├── Filekin.Core/                   platform-neutral domain and application logic
└── Filekin.Infrastructure.Windows/ Windows, PowerShell, and terminal integration

tests/
├── Filekin.Core.Tests/             platform-neutral unit tests
└── Filekin.Infrastructure.Windows.Tests/
                                      Windows integration tests
```

The dependency direction is `Filekin.App → Filekin.Core` and `Filekin.App → Filekin.Infrastructure.Windows → Filekin.Core`. Platform-specific APIs stay out of `Filekin.Core`.

## Prerequisites

- Windows development environment
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0); `global.json` selects SDK 10.0.400 with patch roll-forward

## Build and test

From the repository root:

```powershell
dotnet restore Filekin.sln
dotnet build Filekin.sln --configuration Release --no-restore
dotnet test Filekin.sln --configuration Release --no-build
dotnet format Filekin.sln --verify-no-changes --no-restore
```

Warnings are treated as errors and the .NET analyzers and repository code-style rules run during builds.

## Contributing and security

See `CONTRIBUTING.md` before proposing a change. Report security issues using the process in `SECURITY.md`.

## License

Filekin is free software licensed under the GNU General Public License version 3. See `LICENSE`.
