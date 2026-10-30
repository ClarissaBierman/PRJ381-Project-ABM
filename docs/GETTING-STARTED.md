# Getting started

This gets you from nothing to a running dashboard and passing tests. Allow about 30 minutes the first time, most of it installing.

## 1. Install the tools

| Tool | Why | Where |
|---|---|---|
| **Visual Studio 2022** (Community is free) | Editing, running and debugging | visualstudio.microsoft.com. In the installer tick the **ASP.NET and web development** workload. |
| **.NET 8 SDK** | Builds and runs the code | Comes with the Visual Studio workload above. Check with `dotnet --version` (should start with 8). |
| **GitHub Desktop** | Getting the code, branches, commits, pull requests | desktop.github.com |
| *(optional)* **DB Browser for SQLite** | Looking inside the database | sqlitebrowser.org |

The first build downloads NuGet packages (xUnit, YamlDotNet, Microsoft.Data.Sqlite) and the page loads Chart.js and SignalR from a CDN, so you need internet the first time.

## 2. Get the code

1. Open GitHub Desktop and sign in.
2. **File > Clone repository**, pick the project repository, choose a folder and click **Clone**.
3. Click **Open in Visual Studio** (or double-click `PRJ381-Project-ABM.sln` in the folder).

Tip: don't put the project in a OneDrive folder if you can help it. OneDrive sometimes locks files while Visual Studio is building.

## 3. Run the dashboard

1. In **Solution Explorer**, right-click **AbmFramework.Dashboard** and choose **Set as Startup Project**.
2. Press **F5** (or the green play button).
3. Your browser opens the dashboard. If it doesn't, open the address shown in the console window, for example `https://localhost:59487`.

Try it: pick **Wolf-Sheep Predation**, choose the **Stable populations** preset, type `7` as the random seed, press **Setup**, then **Run**.

From the command line instead:

```bash
dotnet run --project AbmFramework.Dashboard
```

## 4. Run the tests

- Visual Studio: **Test > Test Explorer**, then **Run All Tests**. All 177 should go green.
- Command line, in the solution folder: `dotnet test`

More about the tests: [TESTING.md](TESTING.md).

## 5. Run a scenario file without the dashboard

The `ConfigDemo` console app runs a JSON or YAML scenario file:

```bash
cd config-module/ConfigDemo
dotnet run -- --config ../configs/sir-default.json
dotnet run -- --config ../configs/wolf-sheep-stable.json --ticks 100
dotnet run -- --config ../configs/schelling-default.json --batch 20
```

In Visual Studio: set **ConfigDemo** as the startup project and press F5. The arguments come from `ConfigDemo/Properties/launchSettings.json`.

## 6. Find your way around

Read [ARCHITECTURE.md](ARCHITECTURE.md) first. Then open these files in this order. Each one starts with a comment explaining what it does:

1. `ABM.Core/Agent.cs` - what an agent is
2. `ABM.Core/SIRAgent.cs` - the simplest model
3. `config-module/AbmFramework.Engine/SimulationEngine.cs` - how a run works
4. `AbmFramework.Dashboard/LiveSimulation.cs` - how the dashboard runs it live
5. `AbmFramework.Dashboard/wwwroot/index.html` - the page (the script at the bottom has a table of contents)

Hover over any class or method in Visual Studio to see its description.

## Problems?

| Problem | Fix |
|---|---|
| The page shows an old version after you changed a .js file | Press **Ctrl+F5** in the browser to reload without the cache, or bump the `?v=` number on that file's `<script>` line in `index.html`. |
| "Could not connect to the simulation server" | The dashboard isn't running, or the page is open from an old run. Start it again with F5. |
| Past runs says saving is off | The database couldn't be opened. The message shows the path and the reason. Check the folder exists and isn't read-only. |
| Build error about a missing package | Make sure you're online and do **Build > Rebuild Solution** (or `dotnet restore`). |
| A NuGet or CDN download is blocked on campus Wi-Fi | Try a different network or a phone hotspot for the first build. |
