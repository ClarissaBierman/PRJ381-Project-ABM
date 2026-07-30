using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace ABM.Persistence.Database
{
    public static class DatabaseInitializer
    {
        public static void Initialize(string databasePath)
        {
            var connectionString = $"Data Source={databasePath}";

            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            CreateSimulationsTable(connection);
            CreateTickRecordsTable(connection);
            CreateAgentStatesTable(connection);
        }

        private static void CreateSimulationsTable(SqliteConnection connection)
        {
            const string sql = @"
                CREATE TABLE IF NOT EXISTS Simulations
                (
                    Id TEXT PRIMARY KEY,
                    StartedAt TEXT NOT NULL,
                    EndedAt TEXT,
                    ScenarioName TEXT NOT NULL,
                    ConfigurationJson TEXT NOT NULL
                );";

            ExecuteNonQuery(connection, sql);
        }

        private static void CreateTickRecordsTable(SqliteConnection connection)
        {
            const string sql = @"
                CREATE TABLE IF NOT EXISTS TickRecords
                (
                    SimulationId TEXT NOT NULL,
                    TickNumber INTEGER NOT NULL,
                    Susceptible INTEGER NOT NULL,
                    Infected INTEGER NOT NULL,
                    Recovered INTEGER NOT NULL,

                    PRIMARY KEY (SimulationId, TickNumber),

                    FOREIGN KEY (SimulationId)
                        REFERENCES Simulations(Id)
                );";

            ExecuteNonQuery(connection, sql);
        }

        private static void CreateAgentStatesTable(SqliteConnection connection)
        {
            const string sql = @"
                CREATE TABLE IF NOT EXISTS AgentStates
                (
                    SimulationId TEXT NOT NULL,
                    TickNumber INTEGER NOT NULL,
                    AgentId INTEGER NOT NULL,
                    X INTEGER NOT NULL,
                    Y INTEGER NOT NULL,
                    HealthState TEXT NOT NULL,

                    PRIMARY KEY (SimulationId, TickNumber, AgentId),

                    FOREIGN KEY (SimulationId)
                        REFERENCES Simulations(Id)
                );";

            ExecuteNonQuery(connection, sql);
        }

        private static void ExecuteNonQuery(SqliteConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }
}

