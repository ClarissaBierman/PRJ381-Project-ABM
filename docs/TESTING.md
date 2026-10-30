# Testing

The project has **177 automated tests** written with **xUnit**. They all run in a few seconds, so run them before every commit.

## Running them

- **Visual Studio:** **Test > Test Explorer**, then **Run All Tests** (the double play button). Green = pass, red = fail. Double-click a failed test to jump to it.
- **Command line**, in the solution folder: `dotnet test`
- Just one project: `dotnet test ABM.Core.Tests`

## What is tested where

| Project | Tests | What they check |
|---|---|---|
| `ABM.Core.Tests` | 45 | Each agent on its own: SIR infection and recovery, Schelling happiness, Boids rules and edges, ant food and pheromone, sheep and wolf energy, eating, offspring and movement. |
| `config-module/AbmFramework.Config.Tests` | 37 | Loading JSON and YAML files, every validation rule, and `SimulationConfig.Clone()`. |
| `config-module/AbmFramework.Engine.Tests` | 82 | The engine with real runs: each model's setup and statistics, same seed = same run, pause/resume/step/reset, schedulers, Monte Carlo batches and the Compare maths. |
| `ABM.Persistence.Tests` | 13 | Saving and reading runs, ticks, agents, batches and deleting, plus the Excel, CSV and JSON exports. Each test uses its own temporary database file. |

The dashboard itself (the web page and SignalR) has no automated tests. Test it by hand, see the checklist in [ADDING-A-MODEL.md](ADDING-A-MODEL.md#checking-your-work).

## How the tests are written

Most tests follow **Arrange, Act, Assert**. A typical one (inside `SIRAgentTests`, where `grid` is a field of the class):

```csharp
[Fact]
public void InfectedAgent_RecoversAfterRecoveryTime()
{
    // Arrange: set up the objects
    var agent = new SIRAgent(0, (5, 5), infectionProbability: 0.5, recoveryTime: 3);
    agent.Infect();

    // Act: do the thing being tested
    for (int i = 0; i < 3; i++) agent.Step(Array.Empty<Agent>(), new Random(1), grid);

    // Assert: check the result
    Assert.Equal(HealthState.Recovered, agent.State);
}
```

- `[Fact]` is one test.
- `[Theory]` with `[InlineData(...)]` runs the same test once per data row. `[MemberData(nameof(TestConfigs.AllModels), ...)]` runs it once per model.
- Name tests `What_WhenSomething_ExpectedResult` so a failing test explains itself.

## Two helpers worth knowing

**`ABM.Core.Tests/FixedRandom.cs`**: a fake `Random` whose every roll returns the same number. A roll of `0.0` passes every "chance of X" check and `0.999` fails it. Use it to test "what happens when the sheep DOES reproduce" without hunting for a lucky seed.

**`config-module/AbmFramework.Engine.Tests/TestConfigs.cs`**: a small, fast config for each model (`TestConfigs.For(ModelType.WolfSheep)`), with a fixed seed. Its settings are deliberately different from the defaults, so a test can tell whether a setting really reached the engine.

## Writing a new test

1. Find the right test file (or add a new class next to the others).
2. Add a `public void` method with `[Fact]`.
3. Use a fixed seed or `FixedRandom` so the result is always the same. A test that sometimes fails is worse than no test.
4. Run it and make sure it fails if you break the code it tests (comment a line out and try). Then put the line back.
