using System.Text.Json;
using ABM.Persistence.Export;
using Microsoft.AspNetCore.Mvc;

namespace AbmFramework.Dashboard.Controllers;

// Turns rows built on the page (the Compare summary) into an Excel file.
[ApiController]
[Route("api/export")]
public class ExportController : ControllerBase
{
    public const int MaxRows = 200_000;

    public class ExcelRequest
    {
        public string FileName { get; set; } = "export.xlsx";
        public string SheetName { get; set; } = "Sheet1";
        public List<List<JsonElement>> Rows { get; set; } = new();
    }

    [HttpPost("xlsx")]
    [RequestSizeLimit(50_000_000)]
    public IActionResult ToExcel([FromBody] ExcelRequest request)
    {
        if (request.Rows.Count > MaxRows) return BadRequest("That is too many rows for one file.");

        var rows = request.Rows
            .Select(row => (IReadOnlyList<object?>)row.Select(Value).ToArray())
            .ToList();

        var name = Path.GetFileNameWithoutExtension(request.FileName);
        var safe = string.Concat(name.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'));
        return File(ExcelExporter.Write(rows, request.SheetName), ExcelExporter.ContentType, (safe.Length > 0 ? safe : "export") + ".xlsx");
    }

    private static object? Value(JsonElement cell) => cell.ValueKind switch
    {
        JsonValueKind.Number => cell.GetDouble(),
        JsonValueKind.String => cell.GetString(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null
    };
}
