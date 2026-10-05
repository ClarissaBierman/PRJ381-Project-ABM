using System;
using System.Collections.Generic;
using System.Globalization;
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
            using var writer = new StreamWriter(filePath);
            await WriteTicksAsync(ticks, writer);
        }

        // SIR runs get Tick,Susceptible,Infected,Recovered. Runs whose ticks
        // carry metrics (every other model) get Tick plus one column per
        // metric key instead, since their S/I/R counts are always 0.
        public static async Task WriteTicksAsync(
            IEnumerable<TickRecord> ticks,
            TextWriter writer)
        {
            var records = ticks.ToList();

            var metricKeys = records
                .SelectMany(r => r.Metrics.Keys)
                .Distinct()
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToList();

            var builder = new StringBuilder();

            if (metricKeys.Count == 0)
            {
                builder.AppendLine("Tick,Susceptible,Infected,Recovered");

                foreach (var tick in records)
                {
                    builder.AppendLine(
                        $"{tick.TickNumber}," +
                        $"{tick.Susceptible}," +
                        $"{tick.Infected}," +
                        $"{tick.Recovered}");
                }
            }
            else
            {
                builder.AppendLine("Tick," + string.Join(",", metricKeys));

                foreach (var tick in records)
                {
                    builder.Append(tick.TickNumber);
                    foreach (var key in metricKeys)
                    {
                        // Invariant culture so decimals always use '.', even
                        // on machines whose locale uses ',' (e.g. en-ZA).
                        var value = tick.Metrics.TryGetValue(key, out var v) ? v : 0.0;
                        builder.Append(',').Append(value.ToString(CultureInfo.InvariantCulture));
                    }
                    builder.AppendLine();
                }
            }

            await writer.WriteAsync(builder.ToString());
        }
    }
}
