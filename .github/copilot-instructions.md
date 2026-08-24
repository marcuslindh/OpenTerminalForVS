# Copilot Instructions for OpenTerminalForVS

## Project Overview

OpenTerminalForVS is a Terminal.Gui-based Windows utility that:
- Detects all open Visual Studio instances via COM interop (Running Object Table + IDispatch)
- Displays their solution directories in a terminal UI
- Launches PowerShell (Enter / double-click), Explorer (E key), or a Claude Code Remote Control session (C key) in the selected directory

This is a **single-file console application** (`Program.cs`) with no traditional architecture layers.

## Build and Run

**Build:**
```cmd
dotnet build
```

**Run:**
```cmd
dotnet run
```

**Publish (NativeAOT, single self-contained exe) — use the script:**
```powershell
.\publish.ps1            # -Runtime win-arm64 / -OutputDir dist / -Run
```
`publish.cmd` is a cmd wrapper for the same script. Output lands in `publish\OpenTerminalForVS.exe`.
The script prepends the Visual Studio Installer directory to `PATH` because the native link step calls
`vswhere.exe` and otherwise fails with MSB3073. Requires the MSVC linker (Desktop development with C++).

The VS publish profile `Properties/PublishProfiles/FolderProfile.pubxml` does the same thing, and must not
set `PublishSingleFile` / `PublishReadyToRun` / `PublishTrimmed` — AOT implies trimming and
`PublishTrimmed=false` breaks the build.

**Requirements:**
- .NET 10 SDK
- Windows (uses COM interop with Visual Studio)
- Visual Studio must be running with at least one open solution

## Key Technical Details

### COM Interop with Visual Studio
- Uses `IRunningObjectTable` to enumerate running VS instances
- Filters ROT entries by `"!VisualStudio"` moniker prefix
- Reads `Solution.IsOpen` and `Solution.FullName` off the DTE object through `IDispatch::GetIDsOfNames` + `Invoke`
- All calls go through raw vtable function pointers (`delegate* unmanaged[Stdcall]<...>`) in `VisualStudioInterop`

**Do not replace this with the `envdte` package.** Built-in COM interop (`[ComImport]` interfaces, RCW casts)
throws `NotSupportedException` under NativeAOT, so the hand-written interop is what makes `PublishAot` work.
The vtable slot numbers are load-bearing — see the comment on each call helper.

### Terminal.Gui Usage
- Creates a single `Window` with a `ListView` component
- `OpenSelectedItem` event (Enter / double-click) launches PowerShell
- `KeyPress` handler matches `'e'`/`'E'` via `KeyEvent.KeyValue` and launches Explorer
- `KeyPress` handler matches `'c'`/`'C'` and launches Claude Code with Remote Control
- `Application.RequestStop()` exits after any action

### Launch Patterns
```csharp
// PowerShell
System.Diagnostics.Process.Start(new ProcessStartInfo
{
    FileName = "powershell.exe",
    Arguments = $"-NoExit -Command cd \"{dir}\"",
    UseShellExecute = true,
    WorkingDirectory = dir
});

// Explorer
System.Diagnostics.Process.Start(new ProcessStartInfo
{
    FileName = "explorer.exe",
    Arguments = $"\"{dir}\"",
    UseShellExecute = true
});

// Claude Code with Remote Control, session named after the folder
System.Diagnostics.Process.Start(new ProcessStartInfo
{
    FileName = "powershell.exe",
    Arguments = $"-NoExit -Command claude --remote-control \"{sessionName}\"",
    UseShellExecute = true,
    WorkingDirectory = dir
});
```

## Conventions

- **Top-level statements:** All code is in `Program.cs` using C# 10+ top-level statements
- **Error handling:** Silent `catch` blocks for VS instances closing during enumeration
- **Comments:** Swedish comments may appear (e.g., "någon instans kan stängas mitt i iteration")
- **No async:** Synchronous COM interop and UI - no async/await patterns used
- **AOT-safe:** No reflection, `dynamic`, or runtime codegen — it would break the published binary

## Dependencies

- **Terminal.Gui** (1.19.0): Console UI framework (trim/AOT warnings from this package at publish time are expected)
