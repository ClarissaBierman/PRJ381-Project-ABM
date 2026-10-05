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
- **Download CSV** of every tick in the current run
- **Compare runs**: run a model up to 100 times with different seeds and see a table, averages, spread, and an average-over-time chart. You can also compare two settings side by side.
- **Resizable layout** that fits on one screen. Drag the lines between panels to resize them.

## Project structure

```
ABM.Core/                      Agents, grid and patches (SIR, Schelling, Boids, Ant, Sheep, Wolf)
ABM.Core.Tests/                Unit tests for the agents
ABM.Persistence/               SQLite storage and CSV export
AbmFramework.Dashboard/        ASP.NET Core web app (SignalR + Chart.js)
  Hubs/                          SimulationHub (live runs), ComparisonHub (Compare runs)
  Comparison/                    Runs batches for the Compare panel
  wwwroot/                       index.html, js/grid.js, js/compare.js, css/
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
7. Click **Download CSV** to save the run's statistics.
8. Click **Compare** to run the model many times and compare the results.

Using the same random seed with the same settings always gives the same run.

## Notes

- **CSV in Excel:** on computers set to South African number formats, Excel may put everything in one column, because it expects `;` between columns instead of `,`. Use **Data → From Text/CSV** and pick **Comma** as the delimiter.
- **Wolf-Sheep** on a small grid usually ends with the wolves wiping out the sheep. Try a bigger grid (for example 50×50) to keep both populations going for longer.
- Grid width and height can be from 5 to 200.
