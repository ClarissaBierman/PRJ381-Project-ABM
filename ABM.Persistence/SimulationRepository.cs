// =============================================================================
// SimulationRepository.cs  (ABM.Persistence)
//
// The SQLite implementation of IRepository: all the SQL in the project lives
// here. See DatabaseInitializer.cs for the tables and docs/DATABASE.md for a
// diagram.
//
// How it works:
//   - Every method opens its own short-lived connection and closes it again.
//     SQLite connections are cheap, and this avoids sharing one connection
//     between threads.
//   - Values are always passed as $parameters, never pasted into the SQL
//     text, which keeps the SQL safe from injection and handles quotes.
//   - Saving many rows happens inside one transaction, which is many times
//     faster than letting SQLite commit after every row.
//   - Guids and dates are stored as text. Dates are always UTC in ISO 8601
//     ("O" format) so they sort correctly as text.
// =============================================================================

using System.Globalization;
using System.Text.Json;
using ABM.Persistence.Models;
using Microsoft.Data.Sqlite;

namespace ABM.Persistence
{
    /// <summary>Saves and loads everything in one SQLite database file.</summary>
    public class SimulationRepository : IRepository
    {
        private readonly string _connectionString;

        /// <summary>The full path of the .db file.</summary>
        public string DatabasePath { get; }

        /// <summary>Opens (and if needed creates) the database at the given path.</summary>
        /// <param name="databasePath">Path to the .db file. Relative paths are made absolute.</param>
        public SimulationRepository(string databasePath)
        {
            DatabasePath = Path.GetFullPath(databasePath);
            _connectionString = $"Data Source={DatabasePath}";

            Database.DatabaseInitializer.Initialize(DatabasePath);
        }

        // Opens a new connection. Foreign keys must be switched on for every
        // connection in SQLite, otherwise ON DELETE CASCADE does nothing.
        private async Task<SqliteConnection> OpenAsync()
        {
            var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using var pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            await pragma.ExecuteNonQueryAsync();

            return connection;
        }

        // ------------------------------------------------------------ saving

        /// <inheritdoc />
        public async Task SaveSimulationAsync(Simulation simulation)
        {
            using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            BindSimulation(command, simulation);
            await command.ExecuteNonQueryAsync();
        }

        /// <inheritdoc />
        public async Task UpdateSimulationAsync(Guid simulationId, string status, DateTime? endedAt, int tickCount)
        {
            using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText =
                @"UPDATE Simulations
                  SET Status = $status, EndedAt = $ended, TickCount = $ticks
                  WHERE Id = $id;";
            command.Parameters.AddWithValue("$status", status);
            command.Parameters.AddWithValue("$ended", Value(endedAt.HasValue ? Date(endedAt.Value) : null));
            command.Parameters.AddWithValue("$ticks", tickCount);
            command.Parameters.AddWithValue("$id", simulationId.ToString());
            await command.ExecuteNonQueryAsync();
        }

        /// <inheritdoc />
        public async Task<int> CloseUnfinishedRunsAsync(string status)
        {
            using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText =
                @"UPDATE Simulations
                  SET Status = $status, EndedAt = IFNULL(EndedAt, $ended)
                  WHERE Status = 'Running';";
            command.Parameters.AddWithValue("$status", status);
            command.Parameters.AddWithValue("$ended", Date(DateTime.UtcNow));
            return await command.ExecuteNonQueryAsync();
        }

        /// <inheritdoc />
        public async Task SaveEnvironmentAsync(EnvironmentRecord environment)
        {
            using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            BindEnvironment(command, environment);
            await command.ExecuteNonQueryAsync();
        }

        /// <inheritdoc />
        public Task SaveTickRecordAsync(TickRecord record) => SaveTickRecordsAsync(new[] { record });

        /// <inheritdoc />
        public async Task SaveTickRecordsAsync(IReadOnlyList<TickRecord> records)
        {
            if (records.Count == 0) return;

            using var connection = await OpenAsync();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;

            foreach (var record in records)
            {
                BindTick(command, record);
                await command.ExecuteNonQueryAsync();
            }

            transaction.Commit();
        }

        /// <inheritdoc />
        public async Task SaveAgentsAsync(IReadOnlyList<AgentRecord> agents)
        {
            if (agents.Count == 0) return;

            using var connection = await OpenAsync();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                @"INSERT INTO Agents (SimulationId, AgentId, Breed, FirstTick, LastTick)
                  VALUES ($simulationId, $agentId, $breed, $first, $last)
                  ON CONFLICT (SimulationId, AgentId) DO UPDATE SET LastTick = excluded.LastTick;";

            // "ON CONFLICT ... DO UPDATE" is an "upsert": insert the agent, or if
            // it is already saved, just move its LastTick forward.

            foreach (var agent in agents)
            {
                command.Parameters.Clear();
                command.Parameters.AddWithValue("$simulationId", agent.SimulationId.ToString());
                command.Parameters.AddWithValue("$agentId", agent.AgentId);
                command.Parameters.AddWithValue("$breed", agent.Breed);
                command.Parameters.AddWithValue("$first", agent.FirstTick);
                command.Parameters.AddWithValue("$last", agent.LastTick);
                await command.ExecuteNonQueryAsync();
            }

            transaction.Commit();
        }

        /// <inheritdoc />
        public async Task SaveAgentStatesAsync(IReadOnlyList<AgentState> states)
        {
            if (states.Count == 0) return;

            using var connection = await OpenAsync();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                @"INSERT OR REPLACE INTO AgentStates (SimulationId, TickNumber, AgentId, X, Y, State, PropertiesJson)
                  VALUES ($simulationId, $tick, $agentId, $x, $y, $state, $properties);";

            foreach (var state in states)
            {
                command.Parameters.Clear();
                command.Parameters.AddWithValue("$simulationId", state.SimulationId.ToString());
                command.Parameters.AddWithValue("$tick", state.TickNumber);
                command.Parameters.AddWithValue("$agentId", state.AgentId);
                command.Parameters.AddWithValue("$x", state.X);
                command.Parameters.AddWithValue("$y", state.Y);
                command.Parameters.AddWithValue("$state", state.State);
                command.Parameters.AddWithValue("$properties", Value(state.PropertiesJson));
                await command.ExecuteNonQueryAsync();
            }

            transaction.Commit();
        }

        /// <inheritdoc />
        public async Task SavePatchesAsync(IReadOnlyList<PatchRecord> patches)
        {
            if (patches.Count == 0) return;

            using var connection = await OpenAsync();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                @"INSERT OR REPLACE INTO Patches (EnvironmentId, TickNumber, X, Y, PropertiesJson)
                  VALUES ($environmentId, $tick, $x, $y, $properties);";

            foreach (var patch in patches)
            {
                command.Parameters.Clear();
                command.Parameters.AddWithValue("$environmentId", patch.EnvironmentId.ToString());
                command.Parameters.AddWithValue("$tick", patch.TickNumber);
                command.Parameters.AddWithValue("$x", patch.X);
                command.Parameters.AddWithValue("$y", patch.Y);
                command.Parameters.AddWithValue("$properties", patch.PropertiesJson);
                await command.ExecuteNonQueryAsync();
            }

            transaction.Commit();
        }

        /// <inheritdoc />
        public async Task SaveBatchAsync(Batch batch, IReadOnlyList<BatchRun> runs)
        {
            using var connection = await OpenAsync();
            using var transaction = connection.BeginTransaction();

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    @"INSERT INTO Batches
                      (Id, GroupId, Label, Model, CreatedAt, Runs, BaseSeed, TickLimit, ConfigurationJson, PageSettingsJson, SummaryJson)
                      VALUES ($id, $group, $label, $model, $created, $runs, $seed, $ticks, $config, $page, $summary);";
                command.Parameters.AddWithValue("$id", batch.Id.ToString());
                command.Parameters.AddWithValue("$group", batch.GroupId.ToString());
                command.Parameters.AddWithValue("$label", batch.Label);
                command.Parameters.AddWithValue("$model", batch.Model);
                command.Parameters.AddWithValue("$created", Date(batch.CreatedAt));
                command.Parameters.AddWithValue("$runs", batch.Runs);
                command.Parameters.AddWithValue("$seed", batch.BaseSeed);
                command.Parameters.AddWithValue("$ticks", batch.TickLimit);
                command.Parameters.AddWithValue("$config", batch.ConfigurationJson);
                command.Parameters.AddWithValue("$page", Value(batch.PageSettingsJson));
                command.Parameters.AddWithValue("$summary", batch.SummaryJson);
                await command.ExecuteNonQueryAsync();
            }

            // One command object per table, reused for every run: only the
            // parameter values change, which is faster than new commands.
            using var simulationCommand = connection.CreateCommand();
            using var environmentCommand = connection.CreateCommand();
            using var tickCommand = connection.CreateCommand();
            simulationCommand.Transaction = transaction;
            environmentCommand.Transaction = transaction;
            tickCommand.Transaction = transaction;

            foreach (var run in runs)
            {
                BindSimulation(simulationCommand, run.Simulation);
                await simulationCommand.ExecuteNonQueryAsync();

                BindEnvironment(environmentCommand, run.Environment);
                await environmentCommand.ExecuteNonQueryAsync();

                foreach (var tick in run.Ticks)
                {
                    BindTick(tickCommand, tick);
                    await tickCommand.ExecuteNonQueryAsync();
                }
            }

            transaction.Commit();
        }

        // ----------------------------------------------------------- reading

        /// <inheritdoc />
        public async Task<Simulation?> GetSimulationAsync(Guid simulationId)
        {
            using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {SimulationColumns} FROM Simulations WHERE Id = $id;";
            command.Parameters.AddWithValue("$id", simulationId.ToString());

            using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync() ? ReadSimulation(reader) : null;
        }

        /// <inheritdoc />
        public async Task<EnvironmentRecord?> GetEnvironmentAsync(Guid simulationId)
        {
            using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText =
                @"SELECT Id, SimulationId, GridWidth, GridHeight, Topology, Scheduler
                  FROM Environments WHERE SimulationId = $id;";
            command.Parameters.AddWithValue("$id", simulationId.ToString());

            using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            return new EnvironmentRecord
            {
                Id = Guid.Parse(reader.GetString(0)),
                SimulationId = Guid.Parse(reader.GetString(1)),
                GridWidth = reader.GetInt32(2),
                GridHeight = reader.GetInt32(3),
                Topology = reader.GetString(4),
                Scheduler = reader.GetString(5)
            };
        }

        /// <inheritdoc />
        public async Task<TickRecord[]> GetTicksAsync(Guid simulationId)
        {
            using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText =
                @"SELECT TickNumber, Susceptible, Infected, Recovered, TotalPopulation, MetricsJson
                  FROM TickRecords WHERE SimulationId = $id ORDER BY TickNumber;";
            command.Parameters.AddWithValue("$id", simulationId.ToString());

            var ticks = new List<TickRecord>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                ticks.Add(new TickRecord
                {
                    SimulationId = simulationId,
                    TickNumber = reader.GetInt32(0),
                    Susceptible = reader.GetInt32(1),
                    Infected = reader.GetInt32(2),
                    Recovered = reader.GetInt32(3),
                    TotalPopulation = reader.GetInt32(4),
                    Metrics = JsonSerializer.Deserialize<Dictionary<string, double>>(reader.GetString(5))
                              ?? new Dictionary<string, double>()
                });
            }

            return ticks.ToArray();
        }

        /// <inheritdoc />
        public async Task<AgentRecord[]> GetAgentsAsync(Guid simulationId)
        {
            using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText =
                @"SELECT AgentId, Breed, FirstTick, LastTick
                  FROM Agents WHERE SimulationId = $id ORDER BY AgentId;";
            command.Parameters.AddWithValue("$id", simulationId.ToString());

            var agents = new List<AgentRecord>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                agents.Add(new AgentRecord
                {
                    SimulationId = simulationId,
                    AgentId = reader.GetInt32(0),
                    Breed = reader.GetString(1),
                    FirstTick = reader.GetInt32(2),
                    LastTick = reader.GetInt32(3)
                });
            }

            return agents.ToArray();
        }

        /// <inheritdoc />
        public async Task<AgentState[]> GetAgentStatesAsync(Guid simulationId)
        {
            using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText =
                @"SELECT TickNumber, AgentId, X, Y, State, PropertiesJson
                  FROM AgentStates WHERE SimulationId = $id ORDER BY TickNumber, AgentId;";
            command.Parameters.AddWithValue("$id", simulationId.ToString());

            var states = new List<AgentState>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                states.Add(new AgentState
                {
                    SimulationId = simulationId,
                    TickNumber = reader.GetInt32(0),
                    AgentId = reader.GetInt32(1),
                    X = reader.GetInt32(2),
                    Y = reader.GetInt32(3),
                    State = reader.GetString(4),
                    PropertiesJson = reader.IsDBNull(5) ? null : reader.GetString(5)
                });
            }

            return states.ToArray();
        }

        /// <inheritdoc />
        public async Task<PatchRecord[]> GetPatchesAsync(Guid environmentId)
        {
            using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText =
                @"SELECT TickNumber, X, Y, PropertiesJson
                  FROM Patches WHERE EnvironmentId = $id ORDER BY TickNumber, Y, X;";
            command.Parameters.AddWithValue("$id", environmentId.ToString());

            var patches = new List<PatchRecord>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                patches.Add(new PatchRecord
                {
                    EnvironmentId = environmentId,
                    TickNumber = reader.GetInt32(0),
                    X = reader.GetInt32(1),
                    Y = reader.GetInt32(2),
                    PropertiesJson = reader.GetString(3)
                });
            }

            return patches.ToArray();
        }

        /// <inheritdoc />
        public async Task<RunListItem[]> ListSimulationsAsync(string? source = null, string? model = null, int limit = 200)
        {
            using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText =
                $@"SELECT {Prefixed("s")}, IFNULL(e.GridWidth, 0), IFNULL(e.GridHeight, 0)
                   FROM Simulations s
                   LEFT JOIN Environments e ON e.SimulationId = s.Id
                   WHERE ($source IS NULL OR s.Source = $source)
                     AND ($model IS NULL OR s.Model = $model)
                   ORDER BY s.StartedAt DESC
                   LIMIT $limit;";
            command.Parameters.AddWithValue("$source", Value(source));
            command.Parameters.AddWithValue("$model", Value(model));
            command.Parameters.AddWithValue("$limit", limit);

            var items = new List<RunListItem>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                items.Add(new RunListItem
                {
                    Simulation = ReadSimulation(reader),
                    GridWidth = reader.GetInt32(SimulationColumnCount),
                    GridHeight = reader.GetInt32(SimulationColumnCount + 1)
                });
            }

            return items.ToArray();
        }

        /// <inheritdoc />
        public Task<Batch[]> ListBatchesAsync(int limit = 200) =>
            ReadBatchesAsync("ORDER BY CreatedAt DESC LIMIT $limit", includeSummary: false,
                command => command.Parameters.AddWithValue("$limit", limit));

        /// <inheritdoc />
        public Task<Batch[]> GetBatchGroupAsync(Guid groupId) =>
            ReadBatchesAsync("WHERE GroupId = $group ORDER BY Label", includeSummary: true,
                command => command.Parameters.AddWithValue("$group", groupId.ToString()));

        /// <inheritdoc />
        public async Task<Simulation[]> GetBatchRunsAsync(Guid batchId)
        {
            using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {SimulationColumns} FROM Simulations WHERE BatchId = $id ORDER BY RunNumber;";
            command.Parameters.AddWithValue("$id", batchId.ToString());

            var runs = new List<Simulation>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                runs.Add(ReadSimulation(reader));
            }

            return runs.ToArray();
        }

        // ---------------------------------------------------------- deleting

        /// <inheritdoc />
        public async Task<bool> DeleteSimulationAsync(Guid simulationId)
        {
            using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Simulations WHERE Id = $id;";
            command.Parameters.AddWithValue("$id", simulationId.ToString());
            return await command.ExecuteNonQueryAsync() > 0;
        }

        /// <inheritdoc />
        public async Task<int> DeleteBatchGroupAsync(Guid groupId)
        {
            using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Batches WHERE GroupId = $group;";
            command.Parameters.AddWithValue("$group", groupId.ToString());
            return await command.ExecuteNonQueryAsync();
        }

        // ----------------------------------------------------------- helpers

        // The Simulations columns in a fixed order. ReadSimulation reads them by
        // position, so keep the two in sync if you add a column.
        private const string SimulationColumns =
            "Id, Model, ScenarioName, Source, Status, StartedAt, EndedAt, TickCount, Seed, ConfigurationJson, BatchId, RunNumber";

        // How many Simulations columns there are, so ListSimulationsAsync knows
        // where the extra grid-size columns start.
        private static readonly int SimulationColumnCount = SimulationColumns.Split(',').Length;

        // "Id, Model, ..." becomes "s.Id, s.Model, ..." for queries that join tables.
        private static string Prefixed(string alias) =>
            string.Join(", ", SimulationColumns.Split(',').Select(c => $"{alias}.{c.Trim()}"));

        // Builds a Simulation from the current row (columns in SimulationColumns order).
        private static Simulation ReadSimulation(SqliteDataReader reader) => new()
        {
            Id = Guid.Parse(reader.GetString(0)),
            Model = reader.GetString(1),
            ScenarioName = reader.GetString(2),
            Source = reader.GetString(3),
            Status = reader.GetString(4),
            StartedAt = ParseDate(reader.GetString(5)),
            EndedAt = reader.IsDBNull(6) ? null : ParseDate(reader.GetString(6)),
            TickCount = reader.GetInt32(7),
            Seed = reader.IsDBNull(8) ? null : reader.GetInt32(8),
            ConfigurationJson = reader.GetString(9),
            BatchId = reader.IsDBNull(10) ? null : Guid.Parse(reader.GetString(10)),
            RunNumber = reader.IsDBNull(11) ? null : reader.GetInt32(11)
        };

        // Shared by ListBatchesAsync and GetBatchGroupAsync: same columns,
        // different WHERE/ORDER BY (filter) and parameters (bind).
        private async Task<Batch[]> ReadBatchesAsync(string filter, bool includeSummary, Action<SqliteCommand> bind)
        {
            using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            // The summary JSON can be large, so lists select an empty string instead.
            var summary = includeSummary ? "SummaryJson" : "''";
            command.CommandText =
                $@"SELECT Id, GroupId, Label, Model, CreatedAt, Runs, BaseSeed, TickLimit, ConfigurationJson, PageSettingsJson, {summary}
                   FROM Batches {filter};";
            bind(command);

            var batches = new List<Batch>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                batches.Add(new Batch
                {
                    Id = Guid.Parse(reader.GetString(0)),
                    GroupId = Guid.Parse(reader.GetString(1)),
                    Label = reader.GetString(2),
                    Model = reader.GetString(3),
                    CreatedAt = ParseDate(reader.GetString(4)),
                    Runs = reader.GetInt32(5),
                    BaseSeed = reader.GetInt32(6),
                    TickLimit = reader.GetInt32(7),
                    ConfigurationJson = reader.GetString(8),
                    PageSettingsJson = reader.IsDBNull(9) ? null : reader.GetString(9),
                    SummaryJson = reader.GetString(10)
                });
            }

            return batches.ToArray();
        }

        // The Bind... helpers set a command's INSERT SQL and parameters for one
        // row. They clear old parameters first so a command can be reused in a loop.
        private static void BindSimulation(SqliteCommand command, Simulation simulation)
        {
            command.CommandText =
                $@"INSERT INTO Simulations ({SimulationColumns})
                   VALUES ($id, $model, $scenario, $source, $status, $started, $ended, $ticks, $seed, $config, $batch, $run);";
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$id", simulation.Id.ToString());
            command.Parameters.AddWithValue("$model", simulation.Model);
            command.Parameters.AddWithValue("$scenario", simulation.ScenarioName);
            command.Parameters.AddWithValue("$source", simulation.Source);
            command.Parameters.AddWithValue("$status", simulation.Status);
            command.Parameters.AddWithValue("$started", Date(simulation.StartedAt));
            command.Parameters.AddWithValue("$ended", Value(simulation.EndedAt.HasValue ? Date(simulation.EndedAt.Value) : null));
            command.Parameters.AddWithValue("$ticks", simulation.TickCount);
            command.Parameters.AddWithValue("$seed", Value(simulation.Seed));
            command.Parameters.AddWithValue("$config", simulation.ConfigurationJson);
            command.Parameters.AddWithValue("$batch", Value(simulation.BatchId?.ToString()));
            command.Parameters.AddWithValue("$run", Value(simulation.RunNumber));
        }

        private static void BindEnvironment(SqliteCommand command, EnvironmentRecord environment)
        {
            command.CommandText =
                @"INSERT INTO Environments (Id, SimulationId, GridWidth, GridHeight, Topology, Scheduler)
                  VALUES ($id, $simulationId, $width, $height, $topology, $scheduler);";
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$id", environment.Id.ToString());
            command.Parameters.AddWithValue("$simulationId", environment.SimulationId.ToString());
            command.Parameters.AddWithValue("$width", environment.GridWidth);
            command.Parameters.AddWithValue("$height", environment.GridHeight);
            command.Parameters.AddWithValue("$topology", environment.Topology);
            command.Parameters.AddWithValue("$scheduler", environment.Scheduler);
        }

        private static void BindTick(SqliteCommand command, TickRecord record)
        {
            command.CommandText =
                @"INSERT OR REPLACE INTO TickRecords
                  (SimulationId, TickNumber, Susceptible, Infected, Recovered, TotalPopulation, MetricsJson)
                  VALUES ($simulationId, $tick, $susceptible, $infected, $recovered, $total, $metrics);";
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$simulationId", record.SimulationId.ToString());
            command.Parameters.AddWithValue("$tick", record.TickNumber);
            command.Parameters.AddWithValue("$susceptible", record.Susceptible);
            command.Parameters.AddWithValue("$infected", record.Infected);
            command.Parameters.AddWithValue("$recovered", record.Recovered);
            command.Parameters.AddWithValue("$total", record.TotalPopulation);
            command.Parameters.AddWithValue("$metrics", JsonSerializer.Serialize(record.Metrics));
        }

        // SQLite parameters need DBNull.Value rather than C# null.
        private static object Value(object? value) => value ?? DBNull.Value;

        // DateTime -> text for the database (UTC, ISO 8601).
        private static string Date(DateTime value) =>
            value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

        // Text from the database -> DateTime (keeps it as UTC).
        private static DateTime ParseDate(string value) =>
            DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }
}
