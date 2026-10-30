// =============================================================================
// JsonExporter.cs  (ABM.Persistence.Export)
//
// Builds a complete JSON document of a saved run (or a whole Compare batch):
// settings, grid, every tick, and optionally agents, agent states and patches.
// Used by the Past runs "JSON" download, so a run can be archived or loaded
// into another tool.
//
// The database stores some columns as JSON text (ConfigurationJson,
// PropertiesJson, SummaryJson). Parse() turns those back into real JSON
// objects so they are nested properly instead of appearing as escaped strings.
// =============================================================================

using System.Text.Json;
using System.Text.Json.Nodes;
using ABM.Persistence.Models;

namespace ABM.Persistence.Export
{
    /// <summary>Turns saved runs and batches into JSON documents.</summary>
    public static class JsonExporter
    {
        // camelCase names and indented output, so the file is easy to read.
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

        /// <summary>
        /// Everything saved for one run, with the stored JSON columns written as
        /// real JSON objects. The optional parts are left out when null.
        /// </summary>
        public static JsonObject BuildRun(
            Simulation simulation,
            EnvironmentRecord? environment,
            IReadOnlyList<TickRecord> ticks,
            IReadOnlyList<AgentRecord>? agents = null,
            IReadOnlyList<AgentState>? agentStates = null,
            IReadOnlyList<PatchRecord>? patches = null)
        {
            var run = new JsonObject
            {
                ["id"] = simulation.Id,
                ["model"] = simulation.Model,
                ["scenarioName"] = simulation.ScenarioName,
                ["source"] = simulation.Source,
                ["status"] = simulation.Status,
                ["startedAt"] = simulation.StartedAt,
                ["endedAt"] = simulation.EndedAt,
                ["tickCount"] = simulation.TickCount,
                ["seed"] = simulation.Seed,
                ["batchId"] = simulation.BatchId,
                ["runNumber"] = simulation.RunNumber,
                ["configuration"] = Parse(simulation.ConfigurationJson)
            };

            if (environment is not null)
            {
                run["environment"] = new JsonObject
                {
                    ["gridWidth"] = environment.GridWidth,
                    ["gridHeight"] = environment.GridHeight,
                    ["topology"] = environment.Topology,
                    ["scheduler"] = environment.Scheduler
                };
            }

            run["ticks"] = new JsonArray(ticks.Select(t =>
            {
                var tick = new JsonObject { ["tick"] = t.TickNumber, ["totalPopulation"] = t.TotalPopulation };
                // SIR ticks have no metrics, so write its S/I/R counts instead.
                if (t.Metrics.Count == 0)
                {
                    tick["susceptible"] = t.Susceptible;
                    tick["infected"] = t.Infected;
                    tick["recovered"] = t.Recovered;
                }
                foreach (var (key, value) in t.Metrics)
                {
                    tick[key] = value;
                }
                return (JsonNode)tick;
            }).ToArray());

            if (agents is not null)
            {
                run["agents"] = new JsonArray(agents.Select(a => (JsonNode)new JsonObject
                {
                    ["id"] = a.AgentId,
                    ["breed"] = a.Breed,
                    ["firstTick"] = a.FirstTick,
                    ["lastTick"] = a.LastTick
                }).ToArray());
            }

            if (agentStates is not null)
            {
                run["agentStates"] = new JsonArray(agentStates.Select(s =>
                {
                    var state = new JsonObject
                    {
                        ["tick"] = s.TickNumber,
                        ["agentId"] = s.AgentId,
                        ["x"] = s.X,
                        ["y"] = s.Y,
                        ["state"] = s.State
                    };
                    if (s.PropertiesJson is not null)
                    {
                        state["properties"] = Parse(s.PropertiesJson);
                    }
                    return (JsonNode)state;
                }).ToArray());
            }

            if (patches is not null)
            {
                run["patches"] = new JsonArray(patches.Select(p => (JsonNode)new JsonObject
                {
                    ["tick"] = p.TickNumber,
                    ["x"] = p.X,
                    ["y"] = p.Y,
                    ["properties"] = Parse(p.PropertiesJson)
                }).ToArray());
            }

            return run;
        }

        /// <summary>A whole Compare batch: its settings, summary and the given run documents.</summary>
        public static JsonObject BuildBatch(Batch batch, IReadOnlyList<JsonObject> runs) => new()
        {
            ["id"] = batch.Id,
            ["groupId"] = batch.GroupId,
            ["label"] = batch.Label,
            ["model"] = batch.Model,
            ["createdAt"] = batch.CreatedAt,
            ["runs"] = batch.Runs,
            ["baseSeed"] = batch.BaseSeed,
            ["tickLimit"] = batch.TickLimit,
            ["configuration"] = Parse(batch.ConfigurationJson),
            ["summary"] = Parse(batch.SummaryJson),
            ["runResults"] = new JsonArray(runs.Select(r => (JsonNode)r).ToArray())
        };

        /// <summary>Turns a JSON document into indented text, ready to download.</summary>
        public static string Write(JsonNode node) => node.ToJsonString(Options);

        // Stored JSON text -> JSON object (null for an empty column).
        private static JsonNode? Parse(string json) =>
            string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json);
    }
}
