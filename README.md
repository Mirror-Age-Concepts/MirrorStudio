Mirror Studio - Open in Visual Studio

Prerequisites
- .NET 9 SDK installed
- Visual Studio 2022/2023 (or later) with ".NET desktop development" workload

Open the solution in Visual Studio
1. Launch Visual Studio.
2. File -> Open -> Project/Solution.
3. Select the `MirrorStudio.sln` file in the repository root (C:\Users\Mirror Age Concepts\source\repos\MirrorStudio\).
4. Allow NuGet to restore packages when prompted (or right-click the solution -> Restore NuGet Packages).
5. Build the solution (Build -> Build Solution).

Set startup project and run
- In Solution Explorer, right-click `MirrorStudio.App` -> `Set as StartUp Project` to make the desktop app the default when launching from Visual Studio.
- Press F5 or Debug -> Start Debugging to run.

Quick run from repository root
- Use the included helper scripts to run the app from the command line:
  - Windows CMD: `run.bat`
  - PowerShell: `.\run.ps1`
  - Or directly: `dotnet run --project MirrorStudio.App/MirrorStudio.App.csproj`

Run from command line
- Restore: `dotnet restore`
- Build: `dotnet build`
- Run Editor: `dotnet run --project Editor/Editor.csproj`
- Run Console app: `dotnet run --project MirrorStudio.App/MirrorStudio.App.csproj`
- Run tests: `dotnet test MirrorStudio.Tests/MirrorStudio.Tests.csproj`

Notes
- The `Editor` project uses Avalonia UI. Visual Studio can run it but ensure the desktop runtime is available on your OS.
- The solution already contains modular projects under the repository root. Add or remove projects as needed via Visual Studio or the `dotnet sln` CLI.

Next steps
- Scaffold the UI shell, New/Open Project dialogs, or implement project persistence. Pick one and I will implement it in the workspace.
