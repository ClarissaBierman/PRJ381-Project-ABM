// =============================================================================
// Program.cs  (AbmFramework.Dashboard)
//
// The web app's start-up code. This is the project you run (set it as the
// startup project in Visual Studio and press F5). It:
//   1. registers the services (one shared instance of each, "singleton"),
//   2. serves the web page from wwwroot (index.html, js, css),
//   3. maps the two SignalR hubs (live updates) and the REST controllers
//      (downloads and Past runs),
//   4. starts the web server.
//
// How the pieces talk:
//   Browser  <-- SignalR /simulationHub -->  SimulationHub  ->  LiveSimulationService  ->  SimulationEngine
//   Browser  <-- SignalR /comparisonHub -->  ComparisonHub  ->  ComparisonService      ->  MonteCarloRunner
//   Browser  --- HTTP /api/...         -->  Controllers    ->  HistoryWriter.Repository (SQLite)
//
// See docs/ARCHITECTURE.md for the full picture.
// =============================================================================

using AbmFramework.Dashboard;
using AbmFramework.Dashboard.Comparison;
using AbmFramework.Dashboard.History;
using AbmFramework.Dashboard.Hubs;

var builder = WebApplication.CreateBuilder(args);

// SignalR = live two-way messages between server and browser (WebSockets).
builder.Services.AddSignalR()
    .AddHubOptions<ComparisonHub>(options =>
    {
        // Lets the page send Cancel while RunComparison is still busy.
        options.MaximumParallelInvocationsPerClient = 2;
    });
builder.Services.AddControllers();

// One instance of each for the whole app, shared by every browser tab.
builder.Services.AddSingleton<HistoryWriter>();
builder.Services.AddSingleton<LiveSimulationService>();
builder.Services.AddSingleton<ComparisonService>();

var app = builder.Build();

// Opens (or creates) the database at startup, so a problem shows in the log straight away.
app.Services.GetRequiredService<HistoryWriter>();
// When the app is closed, save where the live run got to so it can be continued.
app.Lifetime.ApplicationStopping.Register(() => app.Services.GetRequiredService<LiveSimulationService>().SaveForShutdown());

// Serve wwwroot, with index.html as the default page.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHub<SimulationHub>("/simulationHub");
app.MapHub<ComparisonHub>("/comparisonHub");
app.MapControllers();

app.Run();
