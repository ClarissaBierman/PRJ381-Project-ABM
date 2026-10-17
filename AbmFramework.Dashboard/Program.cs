using AbmFramework.Dashboard;
using AbmFramework.Dashboard.Comparison;
using AbmFramework.Dashboard.History;
using AbmFramework.Dashboard.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR()
    .AddHubOptions<ComparisonHub>(options =>
    {
        options.MaximumParallelInvocationsPerClient = 2;
    });
builder.Services.AddControllers();
builder.Services.AddSingleton<HistoryWriter>();
builder.Services.AddSingleton<LiveSimulationService>();
builder.Services.AddSingleton<ComparisonService>();

var app = builder.Build();

// Opens (or creates) the database at startup, so a problem shows in the log straight away.
app.Services.GetRequiredService<HistoryWriter>();
app.Lifetime.ApplicationStopping.Register(() => app.Services.GetRequiredService<LiveSimulationService>().SaveForShutdown());

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHub<SimulationHub>("/simulationHub");
app.MapHub<ComparisonHub>("/comparisonHub");
app.MapControllers();

app.Run();
