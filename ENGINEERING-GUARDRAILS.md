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

## Command Bar Output Guardrails

Do not add a permanently visible output console beneath the Files command bar.

Do not automatically expand large command output into the Files layout.

Do not overload the command bar with routine execution controls; Enter is the primary execution action.

Use transient status, small inline output, rich-view output, or terminal routing according to the adaptive output model in `UX-DESIGN.md`.

## Sidebar Navigation Guardrail

Do not implement the sidebar as an Explorer-style tree.

Do not add Quick Access, This PC, automatic Windows special folders, or an expandable/listed Drives section.

Custom Locations use `@`. Built-in Filekin surfaces such as `/places` and `/drives` appear as direct slash-syntax entries. Selecting a surface changes the main Files content area; filesystem hierarchy remains in the main view.

## Expandable Command Shell Guardrail

For normal finite shell commands, do not create a new output tab/rich view solely to display substantial raw command output.

Use the command bar's explicit expandable shell-output region. The collapsed action is `View`; once expanded, the action is `Collapse`, and Esc must collapse it.

The shell-output region is temporary and must not become a permanently allocated console pane.

## No Speculative UI Chrome Guardrail

Do not introduce UI controls because they are common in Windows Explorer, terminals, IDEs, or generated mockups.

A control must correspond to an approved Filekin behavior. If its purpose is not defined in the product/UX documentation, do not add it.

In particular, keep the Files command bar free of unapproved shell selectors, trash/clear buttons, copy/pop-out buttons, run/play controls, refresh controls, favorites, and arbitrary overflow menus.

Empty space is preferable to unexplained controls.
