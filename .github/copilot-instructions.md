# DeskBorder Copilot Instructions

## Build, test, and lint commands

- **Primary build target:** `dotnet build .\DeskBorder.csproj -p:Platform=ARM64 -v minimal`
- **Secondary compatibility build:** `dotnet build .\DeskBorder.csproj -p:Platform=x64 -v minimal`
- **Tests:** There is currently no dedicated test project in this repository, so there is no full-suite or single-test command yet.
- **Lint / format:** There is currently no standalone lint or formatting command configured in this repository.

Prefer ARM64 for primary validation because that is the main development machine and the main hands-on test platform. Use x64 when you need compatibility confirmation against the historically verified packaged build target in this repository.

## High-level architecture

### App startup and dependency injection

- `Program.cs` enforces single-instance behavior through `Microsoft.Windows.AppLifecycle.AppInstance` and redirects secondary activations back to the main instance.
- `App.xaml.cs` initializes localization and theme overrides before building the DI container, then delegates startup to `IApplicationBootstrapService`.
- `DependencyInjection\ServiceCollectionExtensions.cs` wires almost everything as singletons, including runtime services, settings, tray UI, navigator UI, and the main manage window.

### Runtime control flow

- `ApplicationBootstrapService` is the top-level startup coordinator. It initializes settings, hotkeys, the tray/manage windows, store update checks, and then synchronizes the desired on/off state into the runtime layer.
- `DeskBorderRuntimeService` is the runtime gatekeeper. It turns `IDesktopLifecycleService` on or off and also supports temporary suspensions without losing the requested enabled state.
- `DesktopLifecycleService` is the main orchestration layer for edge activation and hotkeys. It combines:
  - edge state from `IDesktopEdgeMonitorService`
  - virtual desktop operations from `IVirtualDesktopService`
  - navigator refreshes
  - modifier-key consumption
  - pending auto-delete warnings and completion toasts

### Desktop edge monitoring and desktop operations

- `DesktopEdgeMonitorService` samples cursor position, mouse buttons, modifier keys, foreground process state, monitor layout, and raw mouse movement. It decides whether an edge is merely touched or fully activated.
- `MouseMovementTrackingService` feeds the additional edge-trigger-distance feature through Raw Input, so edge activation can depend on real mouse travel instead of only cursor position.
- `VirtualDesktopService` performs actual desktop creation, switching, focused-window moves, auto-delete evaluation, and navigator snapshot generation.

### Virtual desktop interop

- `Interop\VirtualDesktop\VirtualDesktopFoundation.cs` is the only place that should know about COM-level virtual desktop details.
- The interop layer now branches by OS version:
  - Windows 10 (1809 through 22H2)
  - Windows 11
  - Windows 11 24H2 and newer
- `VirtualDesktopHandle` is the abstraction used above the interop layer. Prefer going through `VirtualDesktopFoundation` instead of using version-specific COM interfaces directly in services.

### Settings and UI flow

- `DeskBorderSettings` is an immutable record tree that holds the full app configuration.
- `SettingsService` is the canonical path for loading, normalizing, validating, persisting, importing, and exporting settings. It also applies startup, language, and theme side effects.
- `ManageNavigationService` swaps between the dashboard and settings pages inside the main manage window.
- `SettingsPageViewModel` is the central editor model for settings UI state, selection lists, and hotkey validation feedback.

## Key conventions

### Repository-specific working rules

- Respond to the user in Korean unless they explicitly ask otherwise.
- Do not run `git commit` or `git push` unless the user explicitly requests it.
- Do not run builds for small, localized changes unless the user explicitly asks for validation. Reserve builds for broad, risky, or integration-heavy changes.
- If repository behavior, architecture, build expectations, or conventions change in a way that makes this file outdated, update this instruction file in the same task so it stays aligned with the codebase.

### C# naming and formatting

- Use full, unabbreviated names for variables and methods.
- Private instance fields use `_camelCase`.
- Private static fields use `s_camelCase`.
- Types, properties, and methods use `PascalCase`.
- Keep single-statement `if`, `for`, `foreach`, and `while` bodies on the same line without braces.
- Use expression-bodied members for single-line methods.
- Keep short calls and short signatures on one line instead of wrapping them unnecessarily.
- Use primary constructors where they fit naturally.
- Use collection expressions (`[]`) where possible.

### XAML and event handler conventions

- Event handlers use `On{ControlName}{EventName}` naming.
- For click handlers, use `Clicked` rather than `Click` in the handler name.
- Prefer `Spacing`, `RowSpacing`, and `ColumnSpacing` for layout spacing.
- Use `Margin` only for per-element positional adjustment, not as a substitute for container spacing.

### Settings and persistence conventions

- Treat `DeskBorderSettings` as immutable. Update settings through `SettingsService.UpdateSettingsAsync(...)` rather than mutating nested values in place.
- Let `SettingsService.NormalizeSettings(...)` own defaulting, clamping, blacklist/whitelist reconciliation, and hotkey validation.
- Settings persistence uses source-generated `System.Text.Json` metadata via `DeskBorderSettingsSerializationContext`.

### Runtime and logging conventions

- Important state transitions are logged through `IFileLogService`. Follow the existing `WriteInformation` / `WriteWarning` / `WriteError` patterns for runtime actions and failures.
- `DesktopNavigationActionKind` is behaviorally important. `DesktopLifecycleService` uses it to decide whether a navigation should trigger auto-delete handling.

## NativeAOT and trimming

- This project is configured with `PublishAot=true`, `PublishTrimmed=true`, and `IsAotCompatible=true`.
- Prefer explicit patterns that are NativeAOT-safe:
  - `LibraryImport` for P/Invoke
  - `[GeneratedComInterface]` for COM interop
  - source-generated `JsonSerializerContext` for JSON serialization
- Avoid introducing reflection-heavy patterns, runtime code generation, assembly scanning, or serializer usage that depends on ungenerated runtime metadata.
- If you add new JSON-serialized models, extend an existing source-generated JSON context or add a new one instead of relying on reflection-based serialization.
