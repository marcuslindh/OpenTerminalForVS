

# OpenTerminalForVS

OpenTerminalForVS is a tool that displays a list of all open Visual Studio solutions on your computer and lets you easily open a PowerShell terminal, File Explorer, or a Claude Code session in the folder you pick.

## Features
- Automatically detects all open Visual Studio solutions.
- Displays them in a terminal-based list.
- **Enter** (or double-click) launches PowerShell in the selected solution folder.
- **E** opens the folder in File Explorer.
- **C** opens PowerShell and starts Claude Code with Remote Control enabled, using the solution folder's name as the session name (`claude --remote-control "<folder>"`).
- Clicking with the mouse.

## Building the Project  
1. Build the project (requires .NET 10 SDK):

   ```cmd
   dotnet build
   ```

2. Or publish a single self-contained NativeAOT executable (~3.4 MB, no .NET runtime required):

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
When the program starts, it shows a list of all open Visual Studio solutions. Select a solution and press
**Enter** for PowerShell, **E** for Explorer, or **C** for a Claude Code Remote Control session in that folder.

## Other
- The program uses Terminal.Gui for the user interface.
- Visual Studio instances are found through the COM Running Object Table, called via raw `IUnknown`/`IDispatch`
  vtable pointers so the app can be compiled ahead-of-time (NativeAOT does not support built-in COM interop).
- If no solution is open, a message will be displayed.

Contributions and improvements are welcome!
