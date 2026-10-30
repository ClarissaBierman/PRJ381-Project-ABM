# Adding a new model

This is the most likely thing a new group will do, so here is every place a model touches, in the order to do them. The code has `ADD A MODEL:` comments at each of these spots, so you can also search the solution for that text (**Ctrl+Shift+F** in Visual Studio).

As a running example we'll add **Fire**: a forest where burning trees set their neighbours on fire (NetLogo's "Fire" model).

## Before you start

- Read [ARCHITECTURE.md](ARCHITECTURE.md), especially "Inside one tick" and "Repeatability".
- Look at `SIRAgent.cs`. It is the simplest model and the best one to copy.
- Decide: what are your agents, what does one agent do on its turn, and what numbers do you want to count each tick?

## Checklist

### 1. Config: name the model and its settings

**`config-module/AbmFramework.Config/SimulationConfig.cs`**
- Add your model to the `ModelType` enum: `Fire`.
- Add a property for each setting your model needs, with a sensible default and a `/// <summary>`. Put them in a new `// ---- Fire ----` section.

  ```csharp
  /// <summary>Fraction (0 to 1) of cells that start with a tree.</summary>
  public double TreeDensity { get; set; } = 0.6;
  ```

  The property name is also the name used in JSON/YAML files and on the web page (as `treeDensity`).

**`config-module/AbmFramework.Config/ConfigLoader.cs`**
- In `Validate`, add a block that checks your settings and adds a readable message for each problem:

  ```csharp
  if (config.Model == ModelType.Fire)
  {
      if (config.TreeDensity is < 0.0 or > 1.0)
          errors.Add($"TreeDensity must be between 0.0 and 1.0 (got {config.TreeDensity}).");
  }
  ```

### 2. Core: write the agent

**New file `ABM.Core/FireAgent.cs`**, subclass of `Agent`:
- Store the agent's state in properties.
- Override `Step()`: what the agent does on its turn. Use the `rng` parameter for every random choice (never `new Random()`), otherwise runs stop being repeatable.
- Override `DisplayState`: a short text per state, like `"Burning"`. The web page picks the icon and colour from this text.
- Use `environment.GetPatch(x, y)` if your model keeps data on the grid cells.
- To move, set `Position`. To die, call `Die()`. To have babies, call `QueueOffspring(...)`.

```csharp
public enum TreeState { Green, Burning, Burnt }

public class FireAgent : Agent
{
    public TreeState State { get; private set; } = TreeState.Green;

    public FireAgent(int id, (int X, int Y) position) : base(id, position) { }

    public void Ignite() => State = TreeState.Burning;

    public override string DisplayState => State.ToString();

    public override void Step(IReadOnlyList<Agent> neighbours, Random rng, IEnvironmentManager environment)
    {
        if (State != TreeState.Burning) return;

        // Spread to green neighbours, then burn out.
        foreach (var tree in neighbours.OfType<FireAgent>().Where(t => t.State == TreeState.Green))
            tree.Ignite();

        State = TreeState.Burnt;
    }
}
```

(A tree ignited this tick by an earlier neighbour may still get its own turn in the same tick, because the scheduler order is random. That's a modelling choice to think about, and a good thing to write a test for.)

### 3. Engine: create the agents and count them

**`config-module/AbmFramework.Engine/SimulationEngine.cs`**
- `CreateAgents()`: add `case ModelType.Fire: CreateFireAgents(); break;` and write `CreateFireAgents()`. Look at `CreateSIRAgents()` for one agent per cell, or `CreateWolfSheepAgents()` for agents that can share a cell. Always use `_random` and always call `_grid.Place(agent)`.
- `BuildStatistics()`: add `ModelType.Fire => BuildFireStatistics(),` and write it. Put your numbers in `Metrics`, for example `["Green"]`, `["Burning"]`, `["Burnt"]`. These names appear on the chart, the monitors, the database and the downloads.
- `ExecuteTick()`, at the end: only if something happens to the whole grid every tick by itself (like grass regrowing).

**`config-module/AbmFramework.Engine/ComparisonSummary.cs`**
- `ComparisonColumns.For`: add a case with the columns the Compare panel should show. Without this, Compare throws for your model.

### 4. Dashboard (server side)

**`AbmFramework.Dashboard/ModelCatalog.cs`**
- Add a `DisplayName` ("Forest Fire"), a one-sentence `Description` and a `BuildConfig` case with the settings the model starts with on the dashboard.

The model drop-downs fill themselves from the `ModelType` enum, so there is nothing else to register.

Only if you need them:
- **`LiveSimulation.cs`**, `BuildGridData` / `ToPatchDTO`: extra per-agent values for the inspector (like energy) or a new kind of coloured cell. Also add the field to `Models/AgentDTO.cs` or `Models/PatchDTO.cs`.
- **`History/LiveRunRecorder.cs`**, `Properties` and `CapturePatches`: extra values to save in the database.

### 5. Web page

**`AbmFramework.Dashboard/wwwroot/index.html`** (the script at the bottom)
- `parameterDefinitions`: one entry per slider: `["TreeDensity", "Tree density", 0, 1, 0.01, true]` (the last `true` shows it as a percentage).
- `parameterTips`: the tooltip for each slider.
- *(optional)* `presets`, `monitorTips`, `modelMetricColors`.

**`AbmFramework.Dashboard/wwwroot/js/grid.js`**
- `stateStyles`: one entry for every `DisplayState` your agent can have, with a colour, a legend label and an icon. You can reuse an icon from `iconShapes` or draw a new 32 x 32 SVG there.

**`AbmFramework.Dashboard/wwwroot/js/compare.js`**
- `PARAMS`: the sliders for the Compare panel. `key` must be the camelCase settings name (`treeDensity`), then the server needs no change.

### 6. Tests

- **`config-module/AbmFramework.Engine.Tests/TestConfigs.cs`**: add a small, fast config for your model to `For()`. Several engine tests run once for every model in the enum, so they fail until you do this. That's on purpose, so no model is ever left untested.
- **`ABM.Core.Tests`**: a test class for your agent. Use `FixedRandom` to force random rolls (see `SIRAgentTests.cs`).
- **`config-module/AbmFramework.Engine.Tests/EngineModelTests.cs`**: check the setup and that the statistics add up.
- **`config-module/AbmFramework.Config.Tests/ConfigLoaderTests.cs`**: one test per new validation rule.

### 7. Optional extras

- A scenario file in `config-module/configs`, for example `fire-default.json`, so it can be run with ConfigDemo.
- A row in the model table in the main `README.md`.

## Checking your work

1. **Build > Rebuild Solution**: no errors.
2. Run all tests: all green, including the "all models" ones.
3. Run the dashboard: pick your model, Setup, Run, click an agent, Download, Compare, Past runs, Continue.
4. Run it twice with the same seed: the chart should be identical. If it isn't, something is using randomness that doesn't come from `rng`.
