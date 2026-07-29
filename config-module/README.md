# Config & Scenario Setup — SIR MVP

Implements role 6 from the milestone plan: parses JSON/YAML scenario files
(agent count, infection probability, grid size, tick limit, etc.) into a
validated `SimulationConfig` object ready to hand to the engine, matching
`ISimulationEngine.StartAsync(SimulationConfig config)` from the Milestone 1
design (Section 3.6.6.1).

## Structure

```
AbmFramework.Config/          Class library — the actual config module
  SimulationConfig.cs           the config object itself
  ConfigLoader.cs                JSON/YAML parsing + validation
  ConfigValidationException.cs   carries every validation error at once

AbmFramework.Config.Tests/    xUnit tests for the loader/validator

ConfigDemo/                   Runnable console app — CLI entry point
  Program.cs                    parses --config/--seed/--ticks/--agents,
                                 shows the hand-off to the engine

configs/                      Example scenario files
  sir-default.json / .yaml      baseline scenario (JSON and YAML side by side)
  sir-quick-test.json           small grid/short run, for fast local testing
  sir-high-spread.json          fast, high-infectivity outbreak
```

## Running it

Requires the .NET 8 SDK.

```bash
cd AbmFramework.Config.Tests
dotnet test

cd ../ConfigDemo
dotnet run -- --config ../configs/sir-default.json
dotnet run -- --config ../configs/sir-quick-test.json --seed 99 --ticks 100
```

First build will restore `YamlDotNet` from NuGet automatically.

## SimulationConfig fields

| Field                 | Type           | Default          | Notes |
|-----------------------|----------------|-------------------|-------|
| ScenarioName          | string         | "Default SIR Scenario" | shown in logs, stored on the SIMULATION record |
| GridWidth / GridHeight| int            | 50 / 50           | must be > 0 |
| Topology              | Bounded / Toroidal | Bounded       | matches ENVIRONMENT.topology in the DB schema |
| AgentCount            | int            | 200               | must be > 0 and ≤ GridWidth × GridHeight |
| InitialInfected       | int            | 5                 | must be ≤ AgentCount |
| InfectionProbability  | double         | 0.3               | must be between 0.0 and 1.0 |
| RecoveryTicks         | int            | 10                | SIRAgent's recovery countdown length |
| TickLimit             | int            | 500               | simulation auto-stops after this many ticks |
| RandomSeed            | int? (nullable)| null              | set for reproducible Monte Carlo runs |
| Scheduler             | Sequential / Random | Random       | Random supports Monte Carlo reproducibility via RandomSeed |

A scenario file only needs to specify the fields it wants to change from the
default — see `sir-quick-test.json` for an example that overrides most of
them, vs `sir-default.json` which mirrors the defaults explicitly for
clarity.

## Integrating with your module

**Simulation Engine (role 3):** call `ConfigLoader.LoadAndValidate(path)` at
startup, then pass the resulting `SimulationConfig` straight into
`StartAsync`. `Program.cs` in `ConfigDemo` shows the exact hand-off — it
currently targets a placeholder `ISimulationEngine`/`StubSimulationEngine`
in that same file; once the real engine project exists, swap the project
reference and delete the stub.

**Agent logic (role 1):** `InfectionProbability`, `RecoveryTicks`, and
`InitialInfected` are the three fields `SIRAgent` needs at construction time.

**Persistence (role 7):** `ScenarioName` is what gets written to
`SIMULATION.name`; `GridWidth`/`GridHeight`/`Topology` map to the
`ENVIRONMENT` row.

## Validation behaviour

`ConfigLoader.Validate` collects **every** problem with a config before
throwing `ConfigValidationException`, rather than stopping at the first one
— so if a scenario file has three bad values, you find out about all three
in one run instead of fixing them one at a time. Checks currently enforced:

- Grid dimensions and agent count must be positive
- Agent count must fit on the grid
- Initial infected can't exceed agent count
- Infection probability must be in [0, 1]
- Recovery ticks and tick limit must be positive
- Scenario name can't be blank

## Not yet done / open questions for the team

- No CLI/JSON support yet for multi-run Monte Carlo sweeps (e.g. "run this
  scenario 50 times with different seeds") — easy to add on top of this if
  we need it before the 31st.
- Web UI config form (mentioned in the design doc as an alternative to
  file-based config) is out of scope for this MVP; the JSON file is the
  interface for now.
