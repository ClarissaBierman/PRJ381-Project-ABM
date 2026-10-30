// =============================================================================
// ExcelExporter.cs  (ABM.Persistence.Export)
//
// Writes a real .xlsx file WITHOUT any NuGet package. An .xlsx file is just a
// zip folder of small XML files (the "Office Open XML" format), so this class
// writes the minimum set of those files by hand:
//
//   [Content_Types].xml          what type each file inside the zip is
//   _rels/.rels                  points to the workbook
//   xl/workbook.xml              the workbook, with one sheet
//   xl/_rels/workbook.xml.rels   points to the sheet and the styles
//   xl/styles.xml                two styles: normal (0) and bold (1)
//   xl/worksheets/sheet1.xml     the actual rows and cells
//
// Why not just CSV? On South African regional settings Excel expects ';'
// between columns, so a ',' CSV opens as one squashed column. An .xlsx always
// opens straight into proper columns with real numbers.
//
// You normally don't need to touch the XML. To export something new, build
// a list of rows and call Write(rows).
// =============================================================================

using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using ABM.Persistence.Models;

namespace ABM.Persistence.Export
{
    /// <summary>
    /// Writes a simple .xlsx file. Unlike CSV, Excel opens it straight into
    /// columns with real numbers, whatever the computer's regional settings
    /// (South African settings expect ';' between columns in a CSV).
    /// </summary>
    public static class ExcelExporter
    {
        /// <summary>The MIME type to send with an .xlsx download, so the browser knows what it is.</summary>
        public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        /// <summary>
        /// Builds an .xlsx of a run's ticks. The same columns as the CSV export:
        /// Tick then S/I/R, or Tick then one column per metric.
        /// </summary>
        /// <returns>The file's bytes, ready to send as a download or save to disk.</returns>
        public static byte[] WriteTicks(IEnumerable<TickRecord> ticks, string sheetName = "Ticks")
        {
            var records = ticks.ToList();
            var metricKeys = records
                .SelectMany(r => r.Metrics.Keys)
                .Distinct()
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToList();

            var rows = new List<IReadOnlyList<object?>>();
            if (metricKeys.Count == 0)
            {
                rows.Add(new object?[] { "Tick", "Susceptible", "Infected", "Recovered" });
                rows.AddRange(records.Select(t => (IReadOnlyList<object?>)new object?[] { t.TickNumber, t.Susceptible, t.Infected, t.Recovered }));
            }
            else
            {
                rows.Add(new object?[] { "Tick" }.Concat(metricKeys).ToArray());
                rows.AddRange(records.Select(t => (IReadOnlyList<object?>)new object?[] { t.TickNumber }
                    .Concat(metricKeys.Select(k => (object?)(t.Metrics.TryGetValue(k, out var v) ? v : 0.0)))
                    .ToArray()));
            }

            return Write(rows, sheetName);
        }

        /// <summary>
        /// Builds an .xlsx with one sheet from any table of values. Numbers
        /// become number cells, everything else text. The first row is bold and
        /// stays visible when scrolling. Null values leave the cell empty.
        /// </summary>
        /// <param name="rows">The table: a list of rows, each a list of cell values.</param>
        /// <param name="sheetName">The sheet tab name (cleaned up to Excel's rules).</param>
        /// <returns>The file's bytes.</returns>
        public static byte[] Write(IReadOnlyList<IReadOnlyList<object?>> rows, string sheetName = "Sheet1")
        {
            // Build the zip in memory, then return its bytes.
            using var stream = new MemoryStream();
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                Add(zip, "[Content_Types].xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                    "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                    "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                    "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                    "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                    "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                    "</Types>");

                Add(zip, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                    "</Relationships>");

                Add(zip, "xl/workbook.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                    $"<sheets><sheet name=\"{Escape(SafeSheetName(sheetName))}\" sheetId=\"1\" r:id=\"rId1\"/></sheets>" +
                    "</workbook>");

                Add(zip, "xl/_rels/workbook.xml.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                    "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                    "</Relationships>");

                Add(zip, "xl/styles.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                    "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
                    "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
                    "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                    "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                    "<cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/></cellXfs>" +
                    "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
                    "</styleSheet>");

                Add(zip, "xl/worksheets/sheet1.xml", Sheet(rows));
            }

            return stream.ToArray();
        }

        // Builds sheet1.xml: a frozen header row, a column width, then one <row>
        // per row with one <c> (cell) per value. Cells are addressed like A1, B1.
        private static string Sheet(IReadOnlyList<IReadOnlyList<object?>> rows)
        {
            int columns = rows.Count == 0 ? 1 : Math.Max(1, rows.Max(r => r.Count));
            var xml = new StringBuilder();
            xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            xml.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            xml.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
            xml.Append($"<cols><col min=\"1\" max=\"{columns}\" width=\"16\" customWidth=\"1\"/></cols>");
            xml.Append("<sheetData>");

            for (int r = 0; r < rows.Count; r++)
            {
                xml.Append($"<row r=\"{r + 1}\">");
                for (int c = 0; c < rows[r].Count; c++)
                {
                    var value = rows[r][c];
                    if (value is null) continue;

                    var cell = ColumnName(c) + (r + 1);
                    var style = r == 0 ? " s=\"1\"" : "";
                    // "R" (round-trip) format keeps every digit of a double.
                    // Text cells use "inlineStr" so no shared-strings file is needed.
                    if (Number(value) is { } number)
                    {
                        xml.Append($"<c r=\"{cell}\"{style}><v>{number.ToString("R", CultureInfo.InvariantCulture)}</v></c>");
                    }
                    else
                    {
                        xml.Append($"<c r=\"{cell}\"{style} t=\"inlineStr\"><is><t>{Escape(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "")}</t></is></c>");
                    }
                }
                xml.Append("</row>");
            }

            xml.Append("</sheetData></worksheet>");
            return xml.ToString();
        }

        // Returns the value as a double if it is a (finite) number, otherwise null.
        // NaN and infinity aren't allowed in Excel, so they are written as text.
        private static double? Number(object value) => value switch
        {
            int i => i,
            long l => l,
            float f when float.IsFinite(f) => f,
            double d when double.IsFinite(d) => d,
            decimal m => (double)m,
            _ => null
        };

        // 0 -> "A", 25 -> "Z", 26 -> "AA", 27 -> "AB", ... (Excel's column letters).
        private static string ColumnName(int index)
        {
            var name = "";
            for (index++; index > 0; index = (index - 1) / 26)
            {
                name = (char)('A' + (index - 1) % 26) + name;
            }
            return name;
        }

        // Excel sheet names can't contain [ ] : * ? / \ and are at most 31 characters.
        private static string SafeSheetName(string name)
        {
            var cleaned = new string(name.Where(ch => "[]:*?/\\".IndexOf(ch) < 0).ToArray()).Trim();
            if (cleaned.Length == 0) cleaned = "Sheet1";
            return cleaned.Length > 31 ? cleaned[..31] : cleaned;
        }

        // Makes text safe inside XML: removes control characters XML doesn't
        // allow, and turns < > & " ' into their XML escapes.
        private static string Escape(string text)
        {
            var clean = new string(text.Where(ch => ch == '\t' || ch == '\n' || ch == '\r' || ch >= ' ').ToArray());
            return SecurityElement.Escape(clean) ?? "";
        }

        // Adds one text file to the zip, as UTF-8 without a byte-order mark.
        private static void Add(ZipArchive zip, string path, string content)
        {
            var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }
    }
}
