# Engineering Guardrails

These rules are normative for implementation.

## Product Fidelity

When the specification is clear, implement it as written. Do not invent new product behavior during coding.

Do not add unrequested:
- AI features
- slash commands
- panels/views
- settings
- navigation concepts
- automation
- visual flourishes

## UI

WPF is the framework, not the visual identity.

Do not ship stock WPF styling or generic AI-generated dashboard aesthetics.

Target a compact, modern, terminal/developer-tool interface with restrained chrome and strong keyboard support.

## Code

Prefer standard .NET and Windows APIs over custom reinvention.

Use clear responsibility boundaries without over-abstracting.

Avoid:
- god classes
- duplicate logic
- manager/service/pipeline layers without real responsibility
- unnecessary dependencies
- swallowed exceptions
- fake success states
- TODO-heavy features presented as finished
- rewriting stable code solely for stylistic preference

## Platform

Use .NET for ordinary filesystem work.

Use selective Windows APIs for Windows-owned behavior such as Recycle Bin, associations, known folders, UAC, and native Properties.

Do not use Explorer/Shell UI as the product interface.

## Performance

Never block the WPF UI thread with filesystem, process, recursive scan, hashing, or archive work.

Use virtualization for large item collections and asynchronous/background work where appropriate.

## Principle

> Reliable and simple beats clever.

> When the specification is clear, implement the specification. Do not invent the product while coding it.

## PowerShell / Terminal Integration Guardrail

Do not implement the Files command bar as a new PowerShell process per command.

Keep PowerShell SDK/runspace integration behind the shell-backend boundary.

Do not fake terminal behavior inside a rich output view. Real interactive terminal sessions belong behind the terminal-host/ConPTY boundary.

Before broad implementation, complete the required runspace + ConPTY technical spike and record its findings.

Do not silently invent support for non-filesystem PowerShell providers inside the Files hierarchy.

When a non-filesystem provider is requested, keep/restore the Files runspace at its previous filesystem location and create a fresh ConPTY-backed PowerShell terminal initialized at the requested provider path. Do not migrate arbitrary runspace state into the terminal.

Known interactive tools must route before process creation. If an unknown finite command proves interactive, **Run in terminal** starts a fresh process. Do not implement live process promotion into ConPTY or persistent user-defined interactive routing rules in v1.

## Terminal Lifecycle Guardrails

Terminal tabs must host a real root shell through the terminal-host/ConPTY boundary.

Do not:
- make every interactive child tool the terminal's root process
- fake a PowerShell prompt after a child tool exits
- leave dead root-shell sessions displayed as permanent terminal tabs
- keep terminal working directories synchronized with Files after terminal launch
- intercept normal terminal input to emulate Files command-bar syntax

A terminal inherits Files location once at creation and owns its own session thereafter.

## Workspace Surface Guardrails

Reuse workspace hosting and common visual primitives, but do not collapse all views into one generic template.

Do not make rich views look like filesystem folder listings, make task tabs unrelated dashboard-style pages, duplicate Back/Esc/focus/loading/error infrastructure per command, create a bespoke design system for every rich command, or over-abstract content until all surfaces become visually generic.

Share infrastructure and design tokens; keep `FileHierarchySurface`, `RichViewSurface`, and `TaskSurface` semantically distinct.

## Persistent-State Guardrails

Store ordinary user configuration in readable `%AppData%\<AppName>\settings.json`.

Do not:
- hide normal preferences in the Windows Registry
- serialize secrets/tokens/passwords into settings.json
- fill settings.json with framework-generated metadata
- overwrite a malformed user config destructively without preserving/recovering it
- use SQLite for simple configuration merely because a database already exists

Use SQLite transactions for operation-history/undo state that requires consistency.

Configuration schemas should use stable descriptive names and validation.

## Tidy Implementation Guardrails

Implement Tidy natively inside the C# codebase.

Do not:
- call the legacy Tidy executable
- port Desktop icon-positioning logic
- duplicate FileOperationService conflict/permission logic inside TidyEngine
- introduce AI classification for v1
- make Tidy recursively reorganize existing folder hierarchies by default

TidyEngine should remain focused on deterministic classification and organization planning.

## Packaging and Update Guardrails

Ship both:
- a traditional installer
- a portable ZIP

Build both from the same self-contained application payload.

Do not:
- require Microsoft Store distribution
- require users to install .NET separately
- build a custom updater before it is necessary
- silently force updates
- treat portable mode as permission to scatter settings beside the EXE without an explicit product decision
- add paid signing as a v1 dependency
- use self-signed certificates to imply public trust

Prefer a simple installer toolchain such as Inno Setup unless concrete requirements justify more complexity.

Keep update installation user-controlled.

## Open-Source Repository Guardrail

The codebase is intended to be publicly understandable and contributable under GPLv3.

Do not unnecessarily obscure ordinary implementation details or create contributor-hostile build/setup requirements.

Repository setup should document how to build, test, and contribute to the application.

## Product Naming Guardrail

The product is **Filekin**.

Do not rename the visual `Files` workspace merely because the application is named Filekin; `Files` is valid UI/domain terminology for the filesystem surface.

Do not introduce alternate product names, legacy concept names, or generic placeholder branding into new implementation files.
