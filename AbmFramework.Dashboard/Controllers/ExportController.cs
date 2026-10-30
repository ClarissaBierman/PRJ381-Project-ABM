// =============================================================================
// ExportController.cs  (AbmFramework.Dashboard.Controllers)
//
//   POST /api/export/xlsx   body: { fileName, sheetName, rows: [[...], [...]] }
//
// Turns a table the PAGE built (the Compare results) into an Excel file.
// The page already has the numbers, so it just posts the rows and gets an
// .xlsx back. Uses the same ExcelExporter as everything else.
// =============================================================================

using System.Text.Json;
using ABM.Persistence.Export;
using Microsoft.AspNetCore.Mvc;

namespace AbmFramework.Dashboard.Controllers;

/// <summary>Turns rows built on the page (the Compare summary) into an Excel file.</summary>
[ApiController]
[Route("api/export")]
public class ExportController : ControllerBase
{
    /// <summary>Most rows accepted in one file, to protect the server.</summary>
    public const int MaxRows = 200_000;

    /// <summary>What the page posts.</summary>
    public class ExcelRequest
    {
        /// <summary>Suggested file name. Cleaned up to letters, digits, - and _.</summary>
        public string FileName { get; set; } = "export.xlsx";

        /// <summary>The sheet tab name.</summary>
        public string SheetName { get; set; } = "Sheet1";

        /// <summary>The table, row by row. The first row is the (bold) header.</summary>
        public List<List<JsonElement>> Rows { get; set; } = new();
    }

    /// <summary>Builds and returns the .xlsx file.</summary>
    [HttpPost("xlsx")]
    [RequestSizeLimit(50_000_000)]
    public IActionResult ToExcel([FromBody] ExcelRequest request)
    {
        if (request.Rows.Count > MaxRows) return BadRequest("That is too many rows for one file.");

        var rows = request.Rows
            .Select(row => (IReadOnlyList<object?>)row.Select(Value).ToArray())
            .ToList();

        // Never trust a file name from the browser: keep only safe characters.
        var name = Path.GetFileNameWithoutExtension(request.FileName);
        var safe = string.Concat(name.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'));
        return File(ExcelExporter.Write(rows, request.SheetName), ExcelExporter.ContentType, (safe.Length > 0 ? safe : "export") + ".xlsx");
    }

    // A JSON cell -> a number, text or empty cell for ExcelExporter.
    private static object? Value(JsonElement cell) => cell.ValueKind switch
    {
        JsonValueKind.Number => cell.GetDouble(),
        JsonValueKind.String => cell.GetString(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null
    };
}
