

# OpenTerminalForVS

OpenTerminalForVS is a tool that displays a list of all open Visual Studio solutions on your computer and lets you easily open a PowerShell terminal by clicking with the mouse on the folder you want to open.

## Features
- Automatically detects all open Visual Studio solutions.
- Displays them in a terminal-based list.
- Launches PowerShell in the selected solution folder with a single selection.
- Clicking with the mouse.

## Building the Project  
1. Build the project (requires .NET 10 SDK):

   ```´cmd
   dotnet build
   ```

## Running the Program
Run the built executable or use `dotnet run`:

```cmd
dotnet run
```

## Requirements
- .NET 10 SDK
- Visual Studio must be running with at least one open solution

## Usage
When the program starts, it shows a list of all open Visual Studio solutions. Select a solution from the list to open a PowerShell terminal in that folder.

## Other
- The program uses Terminal.Gui for the user interface.
- If no solution is open, a message will be displayed.

Contributions and improvements are welcome!
