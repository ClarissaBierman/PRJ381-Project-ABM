using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ABM.Persistence.Models;
using System.Threading.Tasks;

namespace ABM.Persistence.Export
{
    public static class CsvExporter
    {
        public static async Task ExportTicksAsync(
            IEnumerable<TickRecord> ticks,
            string filePath)
        {
            var builder = new StringBuilder();

            builder.AppendLine("Tick,Susceptible,Infected,Recovered");

            foreach (var tick in ticks)
            {
                builder.AppendLine(
                    $"{tick.TickNumber}," +
                    $"{tick.Susceptible}," +
                    $"{tick.Infected}," +
                    $"{tick.Recovered}");
            }

            await File.WriteAllTextAsync(filePath, builder.ToString());
        }
    }
}
