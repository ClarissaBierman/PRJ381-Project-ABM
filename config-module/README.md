# config-module

Despite the folder name, this holds three parts of the framework (the name comes from how the work was first split up, see [KNOWN-ISSUES.md](../docs/KNOWN-ISSUES.md#small-tidy-ups)):

```
AbmFramework.Config/          SimulationConfig (every setting) and ConfigLoader (JSON/YAML + validation)
AbmFramework.Config.Tests/    Tests for loading and validating settings
AbmFramework.Engine/          SimulationEngine, schedulers, Monte Carlo runner, Compare maths
AbmFramework.Engine.Tests/    Tests for the engine, each model and Compare
ConfigDemo/                   Console app: runs a scenario file without the dashboard
configs/                      Example scenario files
```

Everything is in the main solution, `PRJ381-Project-ABM.sln` in the folder above. See [docs/ARCHITECTURE.md](../docs/ARCHITECTURE.md) for how these fit with the rest.

## Scenario files

A scenario file is a `SimulationConfig` written as JSON or YAML. The names are the C# property names. Anything you leave out keeps its default, so a file only needs the settings it changes.

```json
{
  "ScenarioName": "My test",
  "Model": "SIR",
  "GridWidth": 20,
  "GridHeight": 20,
  "AgentCount": 50,
  "RandomSeed": 42
}
```

| File | What it runs |
|---|---|
| `sir-default.json` / `sir-default.yaml` | The default SIR scenario, in both formats side by side |
| `sir-quick-test.json` | A small, short SIR run for quick testing |
| `sir-high-spread.json` | A fast, high-infection outbreak |
| `schelling-default.json` | Schelling segregation |
| `boids-default.json` | Boids flocking |
| `ant-foraging-default.json` | Ant foraging |
| `wolf-sheep-stable.json` | Wolf-Sheep with the dashboard's "Stable populations" preset and seed 7 |

Every setting, its default and what it means is documented on its property in `AbmFramework.Config/SimulationConfig.cs` (hover over it in Visual Studio). `ConfigLoader.Validate` collects every problem in a file before reporting, so you see all of them at once.

## Running a scenario (ConfigDemo)

From the `ConfigDemo` folder:

```bash
dotnet run -- --config ../configs/sir-default.json
dotnet run -- --config ../configs/sir-quick-test.json --seed 99 --ticks 100
dotnet run -- --config ../configs/schelling-default.json --batch 20
```

| Flag | Meaning |
|---|---|
| `--config <file>` | The scenario file. Required. |
| `--seed <n>` | Use this random seed instead of the file's. |
| `--ticks <n>` | Use this run length instead of the file's. |
| `--agents <n>` | Use this agent count instead of the file's. |
| `--batch <n>` | Run the scenario n times (Monte Carlo) instead of once. |

Results are saved to `simulations.db` next to the program, and a CSV file of the results is written there too. The console prints both paths.

In Visual Studio: set **ConfigDemo** as the startup project and press F5. It runs `sir-default.json` (see `Properties/launchSettings.json`).

## Using the engine from your own code

```csharp
var config = ConfigLoader.LoadAndValidate("configs/sir-default.json");

var engine = new SimulationEngine();
engine.TickCompleted += (sender, stats) => Console.WriteLine(stats);
var history = await engine.RunToCompletionAsync(config);

// Or 100 runs with different seeds:
var results = await new MonteCarloRunner().RunAsync(config, replicateCount: 100, baseSeed: 7);
```
