// =============================================================================
// DatabaseInitializer.cs  (ABM.Persistence.Database)
//
// Creates the SQLite database file and its tables the first time the app
// runs. Called by SimulationRepository's constructor, so you never need to
// call it yourself.
//
// The tables and how they link (every child row is deleted with its parent):
//
//   Batches          one Compare batch (setting A or B)
//     Simulations    one run. BatchId is set for runs that belong to a batch
//       Environments   the run's grid settings (exactly one per run)
//         Patches        cell data (grass, food, ...) at the start and end
//       TickRecords    one row per tick: the statistics
//       Agents         one row per agent that ever existed in the run
//         AgentStates    where an agent was, and its state, at sampled ticks
//
// Ids are GUIDs stored as TEXT. Dates are stored as TEXT too (ISO 8601).
//
// Schema version: SQLite's built-in "user_version" number records which
// version of the tables the file has. If it is older than SchemaVersion, the
// old tables are DROPPED and rebuilt, which deletes all saved runs. So:
// changing a table = bump SchemaVersion (and warn your team their Past runs
// will be wiped), or write a proper migration instead. See docs/DATABASE.md.
// =============================================================================

using Microsoft.Data.Sqlite;

namespace ABM.Persistence.Database
{
    /// <summary>Creates (or upgrades) the database file and its tables.</summary>
    public static class DatabaseInitializer
    {
        /// <summary>
        /// The current version of the tables. Bump this when the tables change.
        /// Older files are rebuilt from scratch (their data is lost).
        /// </summary>
        public const int SchemaVersion = 2;

        /// <summary>
        /// Makes sure the database file exists with all the current tables.
        /// Safe to call every time the app starts: existing tables and data are kept.
        /// </summary>
        /// <param name="databasePath">Path to the .db file. Its folder is created if needed.</param>
        public static void Initialize(string databasePath)
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(databasePath));
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            using var connection = new SqliteConnection($"Data Source={databasePath}");
            connection.Open();

            // Which version of the tables does this file have? A brand new file is 0.
            var version = Convert.ToInt32(Scalar(connection, "PRAGMA user_version;"));
            if (version < SchemaVersion)
            {
                DropOldTables(connection);
            }

            // WAL ("write-ahead log") lets the app read the database (Past runs)
            // while the background writer is saving to it.
            Execute(connection, "PRAGMA journal_mode = WAL;");
            // SQLite ignores FOREIGN KEY rules unless this is switched on.
            Execute(connection, "PRAGMA foreign_keys = ON;");

            // Create every table (IF NOT EXISTS, so existing ones are kept) and
            // record the version, all or nothing.
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
        // Children are dropped before their parents.
        private static void DropOldTables(SqliteConnection connection)
        {
            using var transaction = connection.BeginTransaction();
            foreach (var table in new[] { "Patches", "AgentStates", "Agents", "TickRecords", "Environments", "Simulations", "Batches" })
            {
                Execute(connection, $"DROP TABLE IF EXISTS {table};", transaction);
            }
            transaction.Commit();
        }

        // The SQL that creates each table and index, in parent-before-child order.
        // ADD A TABLE: add its CREATE TABLE here, add it to DropOldTables, and
        // bump SchemaVersion.
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

        // Runs a query that returns a single value.
        private static object? Scalar(SqliteConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return command.ExecuteScalar();
        }

        // Runs a statement that returns nothing (CREATE, DROP, PRAGMA).
        private static void Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }
}
