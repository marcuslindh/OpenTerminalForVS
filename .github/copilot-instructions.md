# Copilot Instructions for OpenTerminalForVS

## Project Overview

OpenTerminalForVS is a Terminal.Gui-based Windows utility that:
- Detects all open Visual Studio instances via COM interop (EnvDTE)
- Displays their solution directories in a terminal UI
- Launches PowerShell (Enter / double-click) or Explorer (E key) in the selected directory

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

**Requirements:**
- .NET 10 SDK
- Windows (uses COM interop with Visual Studio)
- Visual Studio must be running with at least one open solution

## Key Technical Details

### COM Interop with Visual Studio
- Uses `IRunningObjectTable` to enumerate running VS instances
- Filters ROT entries by `"!VisualStudio"` moniker prefix
- Retrieves `EnvDTE.DTE` interface for each VS instance
- Accesses `DTE.Solution.FullName` to get solution file path

### Terminal.Gui Usage
- Creates a single `Window` with a `ListView` component
- `OpenSelectedItem` event (Enter / double-click) launches PowerShell
- `KeyPress` handler matches `'e'`/`'E'` via `KeyEvent.KeyValue` and launches Explorer
- `Application.RequestStop()` exits after either action

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
```

## Conventions

- **Top-level statements:** All code is in `Program.cs` using C# 10+ top-level statements
- **Error handling:** Silent `catch` blocks for VS instances closing during enumeration
- **Comments:** Swedish comments may appear (e.g., "någon instans kan stängas mitt i iteration")
- **No async:** Synchronous COM interop and UI - no async/await patterns used

## Dependencies

- **Terminal.Gui** (1.19.0): Console UI framework
- **EnvDTE** (17.14.40260): Visual Studio automation interface
