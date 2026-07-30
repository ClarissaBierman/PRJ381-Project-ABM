using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ABM.Persistence.Models;
using Microsoft.Data.Sqlite;

namespace ABM.Persistence
{
    public class SimulationRepository : IRepository
    {
        private readonly string _connectionString;

        public SimulationRepository(string databasePath)
        {
            _connectionString = $"Data Source={databasePath}";

            Database.DatabaseInitializer.Initialize(databasePath);
        }
        private async Task<SqliteConnection> CreateOpenConnectionAsync()
        {
            var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            return connection;
        }

        public async Task SaveSimulationAsync(Simulation simulation)
        {
            using var connection = await CreateOpenConnectionAsync();

            var command = connection.CreateCommand();

            command.CommandText =
            @"
        INSERT INTO Simulations
        (
            Id,
            StartedAt,
            EndedAt,
            ScenarioName,
            ConfigurationJson
        )
        VALUES
        (
            $id,
            $started,
            $ended,
            $scenario,
            $config
        );
    ";

            command.Parameters.AddWithValue("$id", simulation.Id.ToString());
            command.Parameters.AddWithValue("$started", simulation.StartedAt.ToString("O"));

            if (simulation.EndedAt.HasValue)
                command.Parameters.AddWithValue("$ended", simulation.EndedAt.Value.ToString("O"));
            else
                command.Parameters.AddWithValue("$ended", DBNull.Value);

            command.Parameters.AddWithValue("$scenario", simulation.ScenarioName);
            command.Parameters.AddWithValue("$config", simulation.ConfigurationJson);

            await command.ExecuteNonQueryAsync();
        }

        public async Task SaveTickRecordAsync(TickRecord record)
        {
            using var connection = await CreateOpenConnectionAsync();

            var command = connection.CreateCommand();

            command.CommandText =
            @"
        INSERT INTO TickRecords
        (
            SimulationId,
            TickNumber,
            Susceptible,
            Infected,
            Recovered
        )
        VALUES
        (
            $simulationId,
            $tick,
            $susceptible,
            $infected,
            $recovered
        );
    ";

            command.Parameters.AddWithValue("$simulationId", record.SimulationId.ToString());
            command.Parameters.AddWithValue("$tick", record.TickNumber);
            command.Parameters.AddWithValue("$susceptible", record.Susceptible);
            command.Parameters.AddWithValue("$infected", record.Infected);
            command.Parameters.AddWithValue("$recovered", record.Recovered);

            await command.ExecuteNonQueryAsync();
        }

        public async Task SaveAgentStatesAsync(AgentState[] states)
        {
            using var connection = await CreateOpenConnectionAsync();

            using var transaction = connection.BeginTransaction();

            foreach (var state in states)
            {
                var command = connection.CreateCommand();
                command.Transaction = transaction;

                command.CommandText =
                @"
            INSERT INTO AgentStates
            (
                SimulationId,
                TickNumber,
                AgentId,
                X,
                Y,
                HealthState
            )
            VALUES
            (
                $simulationId,
                $tick,
                $agentId,
                $x,
                $y,
                $healthState
            );
        ";

                command.Parameters.AddWithValue("$simulationId", state.SimulationId.ToString());
                command.Parameters.AddWithValue("$tick", state.TickNumber);
                command.Parameters.AddWithValue("$agentId", state.AgentId);
                command.Parameters.AddWithValue("$x", state.X);
                command.Parameters.AddWithValue("$y", state.Y);
                command.Parameters.AddWithValue("$healthState", state.HealthState);

                await command.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();

        }

        public async Task<TickRecord[]> GetTicksAsync(Guid simulationId)
        {
            using var connection = await CreateOpenConnectionAsync();

            var command = connection.CreateCommand();

            command.CommandText =
            @"
        SELECT
            TickNumber,
            Susceptible,
            Infected,
            Recovered
        FROM TickRecords
        WHERE SimulationId = $simulationId
        ORDER BY TickNumber;
    ";

            command.Parameters.AddWithValue("$simulationId", simulationId.ToString());

            using var reader = await command.ExecuteReaderAsync();

            var ticks = new List<TickRecord>();

            while (await reader.ReadAsync())
            {
                ticks.Add(new TickRecord
                {
                    SimulationId = simulationId,
                    TickNumber = reader.GetInt32(0),
                    Susceptible = reader.GetInt32(1),
                    Infected = reader.GetInt32(2),
                    Recovered = reader.GetInt32(3)
                });
            }

            return ticks.ToArray();
        }

        public async Task<Simulation?> GetSimulationAsync(Guid simulationId)
        {
            using var connection = await CreateOpenConnectionAsync();

            var command = connection.CreateCommand();

            command.CommandText =
            @"
        SELECT
            Id,
            StartedAt,
            EndedAt,
            ScenarioName,
            ConfigurationJson
        FROM Simulations
        WHERE Id = $id;
    ";

            command.Parameters.AddWithValue("$id", simulationId.ToString());

            using var reader = await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                return null;

            return new Simulation
            {
                Id = Guid.Parse(reader.GetString(0)),
                StartedAt = DateTime.Parse(reader.GetString(1)),
                EndedAt = reader.IsDBNull(2)
                            ? null
                            : DateTime.Parse(reader.GetString(2)),
                ScenarioName = reader.GetString(3),
                ConfigurationJson = reader.GetString(4)
            };
        }
    }
}
