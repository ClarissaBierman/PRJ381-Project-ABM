using Microsoft.Data.Sqlite;

namespace ABM.Persistence.Database
{
    public static class DatabaseInitializer
    {
        // Bump this when the tables change. Older files are rebuilt from scratch.
        public const int SchemaVersion = 2;

        public static void Initialize(string databasePath)
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(databasePath));
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            using var connection = new SqliteConnection($"Data Source={databasePath}");
            connection.Open();

            var version = Convert.ToInt32(Scalar(connection, "PRAGMA user_version;"));
            if (version < SchemaVersion)
            {
                DropOldTables(connection);
            }

            Execute(connection, "PRAGMA journal_mode = WAL;");
            Execute(connection, "PRAGMA foreign_keys = ON;");

            using (var transaction = connection.BeginTransaction())
            {
                foreach (var sql in CreateStatements)
                {
                    Execute(connection, sql, transaction);
                }

                Execute(connection, $"PRAGMA user_version = {SchemaVersion};", transaction);
                transaction.Commit();
            }
        }

        // The first version only had SIR columns, so its tables can't be reused.
        private static void DropOldTables(SqliteConnection connection)
        {
            using var transaction = connection.BeginTransaction();
            foreach (var table in new[] { "Patches", "AgentStates", "Agents", "TickRecords", "Environments", "Simulations", "Batches" })
            {
                Execute(connection, $"DROP TABLE IF EXISTS {table};", transaction);
            }
            transaction.Commit();
        }

        private static readonly string[] CreateStatements =
        {
            @"CREATE TABLE IF NOT EXISTS Batches
            (
                Id TEXT PRIMARY KEY,
                GroupId TEXT NOT NULL,
                Label TEXT NOT NULL,
                Model TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                Runs INTEGER NOT NULL,
                BaseSeed INTEGER NOT NULL,
                TickLimit INTEGER NOT NULL,
                ConfigurationJson TEXT NOT NULL,
                PageSettingsJson TEXT,
                SummaryJson TEXT NOT NULL
            );",

            "CREATE INDEX IF NOT EXISTS IX_Batches_GroupId ON Batches (GroupId);",

            @"CREATE TABLE IF NOT EXISTS Simulations
            (
                Id TEXT PRIMARY KEY,
                Model TEXT NOT NULL,
                ScenarioName TEXT NOT NULL,
                Source TEXT NOT NULL,
                Status TEXT NOT NULL,
                StartedAt TEXT NOT NULL,
                EndedAt TEXT,
                TickCount INTEGER NOT NULL DEFAULT 0,
                Seed INTEGER,
                ConfigurationJson TEXT NOT NULL,
                BatchId TEXT,
                RunNumber INTEGER,

                FOREIGN KEY (BatchId) REFERENCES Batches(Id) ON DELETE CASCADE
            );",

            "CREATE INDEX IF NOT EXISTS IX_Simulations_BatchId ON Simulations (BatchId);",
            "CREATE INDEX IF NOT EXISTS IX_Simulations_StartedAt ON Simulations (StartedAt);",

            @"CREATE TABLE IF NOT EXISTS Environments
            (
                Id TEXT PRIMARY KEY,
                SimulationId TEXT NOT NULL UNIQUE,
                GridWidth INTEGER NOT NULL,
                GridHeight INTEGER NOT NULL,
                Topology TEXT NOT NULL,
                Scheduler TEXT NOT NULL,

                FOREIGN KEY (SimulationId) REFERENCES Simulations(Id) ON DELETE CASCADE
            );",

            @"CREATE TABLE IF NOT EXISTS TickRecords
            (
                SimulationId TEXT NOT NULL,
                TickNumber INTEGER NOT NULL,
                Susceptible INTEGER NOT NULL,
                Infected INTEGER NOT NULL,
                Recovered INTEGER NOT NULL,
                TotalPopulation INTEGER NOT NULL,
                MetricsJson TEXT NOT NULL,

                PRIMARY KEY (SimulationId, TickNumber),
                FOREIGN KEY (SimulationId) REFERENCES Simulations(Id) ON DELETE CASCADE
            );",

            @"CREATE TABLE IF NOT EXISTS Agents
            (
                SimulationId TEXT NOT NULL,
                AgentId INTEGER NOT NULL,
                Breed TEXT NOT NULL,
                FirstTick INTEGER NOT NULL,
                LastTick INTEGER NOT NULL,

                PRIMARY KEY (SimulationId, AgentId),
                FOREIGN KEY (SimulationId) REFERENCES Simulations(Id) ON DELETE CASCADE
            );",

            @"CREATE TABLE IF NOT EXISTS AgentStates
            (
                SimulationId TEXT NOT NULL,
                TickNumber INTEGER NOT NULL,
                AgentId INTEGER NOT NULL,
                X INTEGER NOT NULL,
                Y INTEGER NOT NULL,
                State TEXT NOT NULL,
                PropertiesJson TEXT,

                PRIMARY KEY (SimulationId, TickNumber, AgentId),
                FOREIGN KEY (SimulationId, AgentId) REFERENCES Agents(SimulationId, AgentId) ON DELETE CASCADE
            );",

            @"CREATE TABLE IF NOT EXISTS Patches
            (
                EnvironmentId TEXT NOT NULL,
                TickNumber INTEGER NOT NULL,
                X INTEGER NOT NULL,
                Y INTEGER NOT NULL,
                PropertiesJson TEXT NOT NULL,

                PRIMARY KEY (EnvironmentId, TickNumber, X, Y),
                FOREIGN KEY (EnvironmentId) REFERENCES Environments(Id) ON DELETE CASCADE
            );"
        };

        private static object? Scalar(SqliteConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return command.ExecuteScalar();
        }

        private static void Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }
}
