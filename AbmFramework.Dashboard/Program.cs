using AbmFramework.Dashboard;
using AbmFramework.Dashboard.Comparison;
using AbmFramework.Dashboard.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddControllers();
builder.Services.AddSingleton<LiveSimulationService>();
builder.Services.AddSingleton<ComparisonService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHub<SimulationHub>("/simulationHub");
app.MapHub<ComparisonHub>("/comparisonHub");
app.MapControllers();

app.Run();
