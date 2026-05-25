# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build and Run

```cmd
dotnet build
dotnet run
```

Requires .NET 10 SDK on Windows. Visual Studio must be running with at least one open solution for there to be anything to display.

There are no tests in this repository.

## Architecture

This is a **single-file console application** — all logic lives in `Program.cs` using C# top-level statements. There are no architectural layers; understanding the project means reading `Program.cs` end to end.

The program does three things in sequence:

1. **Enumerate running Visual Studio instances via COM interop.** `GetRunningVisualStudios()` walks the Running Object Table (`IRunningObjectTable` via `ole32.dll`), filters monikers whose display name starts with `!VisualStudio`, and casts each to `EnvDTE.DTE`. Solution paths come from `DTE.Solution.FullName`. The `catch` block in the enumeration loop is intentional — VS instances can close mid-iteration and we silently skip them.

2. **Render a Terminal.Gui `ListView`** of solution directories inside a single `Window`. If no solutions are open, a message label is shown instead.

3. **Launch on explicit user action.** Two triggers, both followed by `Application.RequestStop()`:
   - `OpenSelectedItem` (Enter or double-click) → `powershell.exe -NoExit -Command cd "<dir>"` with `WorkingDirectory` set.
   - `KeyPress` matching `'e'`/`'E'` via `KeyEvent.KeyValue` → `explorer.exe "<dir>"`.

   Note: an earlier version used `SelectedItemChanged` so *any* selection change (arrow key navigation included) launched PowerShell. That was incompatible with having two actions, so the trigger is now explicit.

## Conventions specific to this codebase

- **Swedish comments** appear in the source (e.g. `// någon instans kan stängas mitt i iteration`). Don't translate them unless asked.
- **No async.** COM interop and Terminal.Gui are used synchronously throughout — don't introduce `async`/`await` without reason.
- **Windows-only by design.** The COM interop with EnvDTE and the `powershell.exe` launch are not portable; don't add cross-platform abstractions.

## Dependencies

- `Terminal.Gui` 1.19.0 — console UI (note: 1.x API, not the rewritten 2.x)
- `envdte` 17.14.40260 — Visual Studio automation COM interop
