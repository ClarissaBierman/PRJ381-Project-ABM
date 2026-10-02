using AbmFramework.Dashboard;
using AbmFramework.Dashboard.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddControllers();
builder.Services.AddSingleton<LiveSimulationService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHub<SimulationHub>("/simulationHub");
app.MapControllers();

app.Run();
