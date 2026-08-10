# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build and Run

```cmd
dotnet build
dotnet run
```

Requires .NET 10 SDK on Windows. Visual Studio must be running with at least one open solution for there to be anything to display.

There are no tests in this repository.

### Publishing (Native AOT)

```powershell
.\publish.ps1          # -> bin\Release\net10.0\publish\win-x64\OpenTerminalForVS.exe
.\publish.ps1 -Clean   # wipes obj+bin first, forcing a full ILC + link
```

`PublishAot` is on in the csproj, so plain `dotnet publish -c Release` works too; the script just pins the output path and handles the vswhere/PATH issue below. Either way you get a single self-contained ~3.4 MB exe with no .NET runtime dependency. `dotnet build`/`dotnet run` are unaffected and still JIT.

The native link step needs the MSVC toolchain, which the SDK locates by running `findvcvarsall.bat` → `vcvarsall.bat`. If publishing fails with `'vswhere.exe' is not recognized` followed by `MSB3073` and a mangled `link.exe` command line, the cause is *not* a missing vswhere: `vcvarsall.bat` calls `vswhere.exe` unqualified, and the SDK redirects only stdout to NUL, so the stderr complaint gets captured by `ConsoleToMSBuild` and prepended to the tool path the targets parse out. Fix it by putting the installer directory on `PATH` permanently:

```powershell
[Environment]::SetEnvironmentVariable("PATH",
  [Environment]::GetEnvironmentVariable("PATH","User") + ";C:\Program Files (x86)\Microsoft Visual Studio\Installer", "User")
```

Two AOT warnings from `Terminal.Gui` (IL2104/IL3053, plus IL3000 for `Application.GetSupportedCultures()` reading `Assembly.Location`) are expected and harmless — none of that code is on this app's path. Our own code publishes warning-free.

## Architecture

This is a **single-file console application** — all logic lives in `Program.cs` using C# top-level statements. There are no architectural layers; understanding the project means reading `Program.cs` end to end.

The program does three things in sequence:

1. **Enumerate running Visual Studio instances via COM interop.** The `VisualStudio` static class walks the Running Object Table (`ole32.dll`), filters monikers whose display name starts with `!VisualStudio`, and reads `Solution.IsOpen` / `Solution.FullName` off each one. The `catch` inside the enumeration loop is intentional — VS instances can close mid-iteration and we silently skip them.

   **All COM access is hand-rolled through raw vtable calls** (`delegate* unmanaged<...>`), not `[ComImport]` interfaces. This is a Native AOT requirement, not a stylistic choice: AOT disables built-in COM marshalling (`BuiltInComInterop.IsSupported=false`), so `out IRunningObjectTable` and `obj is DTE` both throw `NotSupportedException` at runtime. `ComWrappers.RegisterForMarshalling(new StrategyBasedComWrappers())` does not rescue it either — `[ComImport]` types lack the vtable stubs it needs. That is why the `envdte` package reference is gone.

   DTE members are reached through `IDispatch` (`GetIDsOfNames` + `Invoke` with `DISPATCH_PROPERTYGET`) rather than the `_DTE` vtable, because dual-interface slot numbers depend on TLB declaration order and are easy to get silently wrong. Vtable slot constants for the interfaces we *do* call directly are named at the top of the class — if you add a call, count the inherited slots (`IMoniker` sits behind `IPersistStream`/`IPersist`, hence `GetDisplayName` at slot 20). Note `IMoniker::GetDisplayName` returns `LPOLESTR` (CoTaskMem), **not** a BSTR; freeing it the wrong way corrupts the heap.

2. **Render a Terminal.Gui `ListView`** of solution directories inside a single `Window`. If no solutions are open, a message label is shown instead.

3. **Launch on explicit user action.** Two triggers, both followed by `Application.RequestStop()`:
   - `OpenSelectedItem` (Enter or double-click) → `powershell.exe -NoExit -Command cd "<dir>"` with `WorkingDirectory` set.
   - `KeyPress` matching `'e'`/`'E'` via `KeyEvent.KeyValue` → `explorer.exe "<dir>"`.

   Note: an earlier version used `SelectedItemChanged` so *any* selection change (arrow key navigation included) launched PowerShell. That was incompatible with having two actions, so the trigger is now explicit.

## Conventions specific to this codebase

- **Swedish comments** appear in the source (e.g. `// någon instans kan stängas mitt i iteration`). Don't translate them unless asked.
- **No async.** COM interop and Terminal.Gui are used synchronously throughout — don't introduce `async`/`await` without reason.
- **Windows-only by design.** The COM interop and the `powershell.exe` launch are not portable; don't add cross-platform abstractions.
- **Keep it AOT-clean.** Don't introduce reflection, `dynamic`, or `[ComImport]` interop — publishing must stay warning-free from our own code. Verify with `dotnet publish -c Release` after touching interop.

## Dependencies

- `Terminal.Gui` 1.19.0 — console UI (note: 1.x API, not the rewritten 2.x)

No COM interop package: `envdte` was removed when the app moved to Native AOT (see Architecture above).
