# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build and Run

```cmd
dotnet build
dotnet run
```

Publish a self-contained NativeAOT executable (single ~3.5 MB `.exe`, no .NET runtime needed):

```powershell
.\publish.ps1            # -Runtime win-arm64 / -OutputDir dist / -Run
```

`publish.ps1` (with `publish.cmd` as a cmd wrapper) writes to `publish\` and is the supported way to build
a release binary. Prefer changing it over telling users a raw `dotnet publish` command line.

The native link step shells out to `vswhere.exe` — when it isn't on `PATH` the publish dies with MSB3073
(`'vswhere.exe' is not recognized`), so the script prepends the Visual Studio Installer directory to `PATH`.
A plain `dotnet publish -c Release` therefore only works from a Developer Command Prompt or with that same
`PATH` fix. `RuntimeIdentifier` is pinned to `win-x64` in the csproj, so no `-r` flag is needed.

The Visual Studio publish profile (`Properties/PublishProfiles/FolderProfile.pubxml`) targets the same
`publish\` folder with `PublishAot`. It must **not** set `PublishSingleFile`, `PublishReadyToRun`, or
`PublishTrimmed` — native compilation implies trimming, and `PublishTrimmed=false` fails the build outright.

Native AOT also requires the MSVC linker (Visual Studio's *Desktop development with C++* workload).

Requires .NET 10 SDK on Windows. For anything to show up there must be at least one open Visual Studio
solution or one running Claude Code session.

There are no tests in this repository.

## Architecture

This is a **single-file console application** — all logic lives in `Program.cs` using C# top-level statements. There are no architectural layers; understanding the project means reading `Program.cs` end to end.

The program does four things in sequence:

1. **Find running Claude Code sessions.** `ClaudeSessionInterop.GetRunningSessions()` reads
   `%USERPROFILE%\.claude\sessions\<pid>.json` — Claude writes one such file per session with `pid`,
   `cwd`, `name`, `status` and `procStart`. Stale files are common, so each pid is checked against the
   process list and, when `procStart` (the process creation time as a FILETIME) is present, against
   `Process.StartTime.ToFileTime()` so a recycled pid can't resurrect a dead session. The JSON is read
   with a small hand-written top-level scanner (`ReadTopLevelValue`) rather than `System.Text.Json`, to
   keep the AOT binary small; it deliberately ignores nested keys (e.g. `formerNames[].name`).

2. **Enumerate running Visual Studio instances via COM interop.** `VisualStudioInterop.GetOpenSolutionDirectories()` walks the Running Object Table (`IRunningObjectTable` via `ole32.dll`), filters monikers whose display name starts with `!VisualStudio`, and reads `Solution.IsOpen` / `Solution.FullName` off each DTE object. The `catch` block in the enumeration loop is intentional — VS instances can close mid-iteration and we silently skip them.

3. **Render two Terminal.Gui `ListView`s** inside one `Window`, each in its own `FrameView`: Visual Studio
   solutions on top, Claude sessions below (`Tab` switches). Either frame shows a message label instead when
   its list is empty. A `*` marks solutions that already have a Claude session running in the same folder.

4. **Launch on explicit user action.** Both lists are wired by the same `WireActions(listView, dirAt)`
   helper, where `dirAt` maps a row index to a folder (the solution directory, or the session's `cwd`).
   Three triggers, each followed by `Application.RequestStop()`:
   - `OpenSelectedItem` (Enter or double-click) → `powershell.exe -NoExit -Command cd "<dir>"` with `WorkingDirectory` set.
   - `KeyPress` matching `'e'`/`'E'` via `KeyEvent.KeyValue` → `explorer.exe "<dir>"`.
   - `KeyPress` matching `'c'`/`'C'` → PowerShell running `claude --remote-control "<folder name>"` in `<dir>`, so the Remote Control session is named after the solution folder.

   Note: an earlier version used `SelectedItemChanged` so *any* selection change (arrow key navigation included) launched PowerShell. That was incompatible with having several actions, so the trigger is now explicit.

## COM interop is hand-written on purpose

`VisualStudioInterop` calls COM through raw vtable function pointers (`delegate* unmanaged[Stdcall]<...>`) and reaches DTE through `IDispatch::GetIDsOfNames` + `Invoke`, rather than through the `envdte` type library.

**This is required by NativeAOT** — built-in COM interop (`[ComImport]` interfaces, RCW casts like `obj is DTE`) throws `System.NotSupportedException: COM Interop requires ComWrapper instance registered for marshalling` in an AOT binary, and registering `StrategyBasedComWrappers` does not help because `[ComImport]` interfaces have no source-generated marshalling. Don't "simplify" this back to `EnvDTE` unless AOT is being dropped.

The vtable slot numbers in the call helpers are load-bearing (e.g. `IMoniker::GetDisplayName` is slot 20 because of the `IPersistStream` base). Each helper has a comment naming the interface method it maps to.

## Conventions specific to this codebase

- **Swedish comments** appear in the source (e.g. `// någon instans kan stängas mitt i iteration`). Don't translate them unless asked.
- **No async.** COM interop and Terminal.Gui are used synchronously throughout — don't introduce `async`/`await` without reason.
- **Windows-only by design.** The COM interop and the `powershell.exe` launch are not portable; don't add cross-platform abstractions.
- **AOT-safe code only.** No reflection, no `dynamic`, no runtime code generation — it would break the published binary.

## Dependencies

- `Terminal.Gui` 1.19.0 — console UI (note: 1.x API, not the rewritten 2.x). It emits trim/AOT analysis warnings (IL2104/IL3053/IL3000) at publish; they come from code paths this app never calls (e.g. `Application.GetSupportedCultures`).
