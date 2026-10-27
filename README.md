# PRJ381 ABM Framework

An agent-based modelling (ABM) framework built in C# and .NET 8, with a live web dashboard inspired by NetLogo. It runs five classic models, shows them on a grid in real time, and can run a model many times to compare results (Monte Carlo).

## Models

| Model | What it shows |
|---|---|
| **SIR Epidemic** | An infection spreads through a population. Agents go from Susceptible to Infected to Recovered. |
| **Schelling Segregation** | Agents move when too few neighbours are like them, and separated groups form. |
| **Boids Flocking** | Birds follow three simple rules (separation, alignment, cohesion), and flocks emerge. |
| **Ant Foraging** | Ants search for food and lay pheromone trails back to the nest that other ants follow. |
| **Wolf-Sheep Predation** | Sheep eat grass and wolves eat sheep. Both spend energy, reproduce and die. |

## Dashboard features

- **Live grid** with an icon for each agent type, coloured patches (grass, food, nest, pheromone) and a legend
- **Simulation settings**: sliders for each model's parameters, plus grid width and height, wrap around edges, scheduling order, run length and random seed
- **Setup, Run and Step** buttons, plus Reset and a speed slider
- **Monitors** showing the current tick and the main values for the model
- **Live chart** of the model's statistics over the whole run
- **Agent inspector**: click an agent to see its state, position, energy or heading
- **Display switches** for pheromone trails, food, grass and energy labels
- **Download** every tick of the current run as an Excel file or a CSV
- **Compare runs**: run a model up to 100 times with different seeds and see a table, averages, spread, and an average-over-time chart. You can also compare two settings side by side.
- **Past runs**: every live run and every comparison is saved to a SQLite database. Open an old run to see its chart again, continue a run that was stopped part way, reopen a comparison with its settings and results, download CSV or JSON, or delete it.
- **Resizable layout** that fits on one screen. Drag the lines between panels to resize them.

## Project structure

```
ABM.Core/                      Agents, grid and patches (SIR, Schelling, Boids, Ant, Sheep, Wolf)
ABM.Core.Tests/                Unit tests for the agents
ABM.Persistence/               SQLite database (repository and tables), CSV and JSON export
ABM.Persistence.Tests/         Tests that save to a temporary database and read it back
AbmFramework.Dashboard/        ASP.NET Core web app (SignalR + Chart.js)
  Hubs/                          SimulationHub (live runs), ComparisonHub (Compare runs)
  Comparison/                    Runs batches for the Compare panel
  History/                       Saves live runs and Compare batches in the background
  Controllers/                   Downloads (CSV and Excel) and the Past runs API (api/history)
  wwwroot/                       index.html, js/grid.js, js/compare.js, js/history.js, css/
config-module/
  AbmFramework.Config/           SimulationConfig and ConfigLoader (JSON/YAML, validation)
  AbmFramework.Config.Tests/     Tests for loading and validating settings
  AbmFramework.Engine/           SimulationEngine, schedulers, Monte Carlo runner
  AbmFramework.Engine.Tests/     Tests for the engine, each model and Compare
  ConfigDemo/                    Small console app to try config files
  configs/                       Example scenario files
```

## Getting started

**You need:** Visual Studio 2022 (with the ASP.NET and web development workload) or the .NET 8 SDK, and an internet connection the first time, so NuGet packages and the Chart.js and SignalR scripts can load.

**Run the dashboard in Visual Studio**
1. Open `PRJ381-Project-ABM.sln`.
2. Set **AbmFramework.Dashboard** as the startup project.
3. Press **F5**. The dashboard opens in your browser.

**Or from the command line**
```bash
dotnet run --project AbmFramework.Dashboard
```
Then open the address shown in the terminal (for example `http://localhost:59488`).

**Run the tests**
- Visual Studio: **Test → Run All Tests**
- Command line: `dotnet test`

## Using the dashboard

1. Pick a model from the **Model** dropdown.
2. Change the sliders, grid size, run length or seed in **Simulation settings** if you want.
3. Click **Setup** to create the agents. They appear on the grid at tick 0.
4. Click **Run** to start (click **Pause** to stop it), or **Step** to run one tick at a time.
5. Use the speed slider to slow it down or speed it up, and **Reset** to clear everything.
6. Click an agent on the grid to inspect it.
7. Click **Download** and pick **Excel** or **CSV** to save the run's statistics.
8. Click **Compare** to run the model many times and compare the results.
9. Click **Past runs** to look at runs and comparisons you did before.

Using the same random seed with the same settings always gives the same run.

## Database

Runs are saved with SQLite, so there is no database server to install. The file is created the first time the dashboard starts:

- Windows: `%LOCALAPPDATA%\PRJ381-ABM\abm.db`
- The exact path is shown at the bottom of the **Past runs** panel and in the console when the dashboard starts.

It is kept outside the project folder on purpose, so it is never committed and OneDrive does not try to sync it while it is being written. To use a different file, set `Database:Path` (for example the environment variable `Database__Path`).

| Table | What it stores |
|---|---|
| **Simulations** | One row per run: model, source (Live, Compare or Console), status, start and end time, tick count, seed and all settings as JSON |
| **Environments** | Grid width and height, wrap around edges and scheduling for each run |
| **TickRecords** | The statistics of every tick, as JSON so it works for every model |
| **Agents** | Every agent in a live run, its breed and the first and last tick it was alive |
| **AgentStates** | Where every agent was and its state, saved at Setup, every 10 ticks and at the end (less often for very big runs) |
| **Patches** | The grass, food, nest and pheromone cells at Setup and at the end of a live run |
| **Batches** | One row per Compare: model, number of runs, base seed, settings and the finished summary. Setting A and B share a GroupId |

Every live run saves the seed it used, even when the seed box is left empty. That is what makes **Continue** work: it replays the run with the same seed and settings up to the tick where it stopped (which gives exactly the same result), pauses there, and the new ticks are added to the same saved run.

Each run in a Compare batch is saved as its own Simulation with all of its ticks, so the results can be checked or exported without running anything again. Saving happens on a background thread, so it doesn't slow the simulation down. A run that is set up but never run is not kept.

`ConfigDemo` saves to its own `simulations.db` next to the program, using the same tables.

## Notes

- **Excel or CSV:** pick **Excel** when you want to open the results in Excel. CSV files use `,` between columns and `.` for decimals, and on computers set to South African number formats Excel puts a CSV in one column. The Excel download doesn't have that problem. Past runs and the Compare panel have both too.
- **Wolf-Sheep** on the default small grid usually ends with the wolves wiping out the sheep. Pick the **Stable populations** preset (50×50 grid, 150 sheep, 25 wolves) to see both populations rise and fall in waves. With those settings both survived 500 ticks in all 30 seeds we tested.
- Every slider has a number box next to it for typing an exact value. Percentages are typed as whole numbers (5 means 5%). Agent sliders only go up to the number of cells on the grid.
- Grid width and height can be from 5 to 200.
