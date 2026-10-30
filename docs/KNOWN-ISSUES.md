# Known issues and ideas for next year

Things we know about but didn't get to, roughly from most to least useful to fix. None of them stop the project from working.

## Worth fixing

**Chart.js and SignalR come from the internet.**
`index.html` loads them from jsDelivr (a CDN) at fixed versions (Chart.js 4.6.1, SignalR 10.0.11), so they won't change by themselves. The catch is the page needs internet the first time. If that's a problem (campus Wi-Fi, a demo with no network), add both with **Add > Client-Side Library** (LibMan) in Visual Studio so they're saved in `wwwroot/lib` and served by the app itself. How to update the versions is explained in the comment above the `<script>` lines in `index.html`.

**Changing a database table wipes all saved runs.**
See [DATABASE.md](DATABASE.md#changing-a-table-careful). Fine for a student project, but write real migrations before anyone relies on the saved data.

**Only one live run at a time.**
`LiveSimulationService` is a singleton, so every browser tab sees and controls the same run. Making it per-user would mean one service per SignalR connection (or per user) and sending each run's messages only to that client (`Clients.Client(id)` or groups instead of `Clients.All`).

**Big grids get slow.**
Grids are capped at 200 x 200 (`ConfigLoader.MaxGridSize`). The server already limits grid messages to about 25 a second and the page draws at most once per screen frame, but a 200 x 200 Wolf-Sheep grid still sends a lot of data. Ideas: send only the cells that changed since the last frame, or a binary format instead of JSON.

## Small tidy-ups

These were left as they are because the code still works and the clean-up only changed comments in these places.

- **`Grid2D.GetNeighbours(int x, int y)`** always wraps around the edges, even on a Bounded grid, while `GetNeighbours(Agent)` (the one the engine uses) respects the topology. Nothing uses the (x, y) version at the moment, but a new model might. Fix: use `ResolvePosition` like the other overload.
- **`SimulationEngine._pauseTokenSource`** is never assigned, only disposed in `Reset()`. It's left over from an earlier pause design and can be deleted.
- **`ConfigLoader.Validate`** checks the SIR settings (InitialInfected, InfectionProbability, RecoveryTicks) for every model, not only SIR. Harmless, because the defaults are valid, but it could be moved into an `if (config.Model == ModelType.SIR)` block like the others.
- **`MonteCarloCsvExporter`** builds the whole file in memory before writing. Fine for hundreds of runs, a `StreamWriter` would be better for huge batches.
- **Folder and namespace names are mixed:** the Engine and Config projects live in a folder called `config-module`, and some namespaces start with `ABM.` while others start with `AbmFramework.`. Renaming touches every file, so if you do it, do it in one pull request at the very start of the year, before anyone else starts working.

## Ideas from our "lessons learnt"

- Number boxes in the Compare panel, like the main settings panel has.
- Let users save their own presets.
- Compare more than two settings at once (a full parameter sweep, like NetLogo's BehaviorSpace).
- Let users add their own models without changing the framework (for example by loading agent classes from a separate project).
- More classic models: fire spread, traffic, predator-prey with more species.
- Run it on a Raspberry Pi, or host it so a whole class can use it. (The Raspberry Pi cluster part of the original plan was out of scope for our year.)
