

# OpenTerminalForVS

OpenTerminalForVS is a tool that displays all open Visual Studio solutions and all running Claude Code sessions on your computer, and lets you easily open a PowerShell terminal, File Explorer, or a Claude Code session in the folder you pick.

## Features
- Automatically detects all open Visual Studio solutions.
- Automatically detects Claude Code sessions running on this computer and shows each one's
  name, status (`idle`/`busy`/`shell`) and working directory.
- Displays both in a terminal-based list; **Tab** switches between the two lists.
- A `*` in front of a solution means a Claude Code session is already running in that folder.
- **Enter** (or double-click) launches PowerShell in the selected solution folder.
- **E** opens the folder in File Explorer.
- **C** opens PowerShell and starts Claude Code with Remote Control enabled, using the folder's name as the session name (`claude --remote-control "<folder>"`).
- Clicking with the mouse.

## Building the Project  
1. Build the project (requires .NET 10 SDK):

   ```cmd
   dotnet build
   ```

2. Or publish a single self-contained NativeAOT executable (~3.5 MB, no .NET runtime required):

   ```powershell
   .\publish.ps1
   ```

   (from `cmd` or by double-clicking: `publish.cmd`)

   The result ends up in `publish\OpenTerminalForVS.exe`. The script also handles the one gotcha in
   the AOT toolchain: the native link step calls `vswhere.exe`, and if it isn't on `PATH` the publish
   fails with `MSB3073`, so the script adds the default Visual Studio Installer directory to `PATH` first.

   Options: `.\publish.ps1 -Runtime win-arm64`, `-OutputDir dist`, `-Run` (starts the exe when done).

   Publishing from the Visual Studio UI (the `FolderProfile` publish profile) produces the same AOT
   executable in the same folder. `dotnet publish -c Release` works too, but then `vswhere.exe`
   must already be on `PATH` — e.g. run it from a Developer Command Prompt.

   Native AOT requires the **Desktop development with C++** workload (MSVC linker) to be installed.

## Running the Program
Run the built executable or use `dotnet run`:

```cmd
dotnet run
```

## Requirements
- .NET 10 SDK (only for building — the published AOT executable is self-contained)
- Visual Studio must be running with at least one open solution
- Claude Code on `PATH` for the **C** option

## Usage
When the program starts, it shows two lists: open Visual Studio solutions on top and running Claude Code
sessions below. **Tab** moves between them. Select a row and press **Enter** for PowerShell, **E** for
Explorer, or **C** for a Claude Code Remote Control session in that folder — the Claude list uses the
session's working directory.

## Other
- The program uses Terminal.Gui for the user interface.
- Visual Studio instances are found through the COM Running Object Table, called via raw `IUnknown`/`IDispatch`
  vtable pointers so the app can be compiled ahead-of-time (NativeAOT does not support built-in COM interop).
- Running Claude Code sessions are read from `%USERPROFILE%\.claude\sessions\<pid>.json`, which Claude
  writes for each session (working directory, name, status). Stale files are filtered out by checking that
  the pid is still alive and was started at the time recorded in the file.
- If no solution is open, or no Claude session is running, a message will be displayed instead.

Contributions and improvements are welcome!
