using AbmFramework.Dashboard;
using AbmFramework.Dashboard.Hubs;
using AbmFramework.Dashboard.Models;

var builder = WebApplication.CreateBuilder(args);

// Register SignalR and the fake simulation background service
builder.Services.AddSignalR();
builder.Services.AddControllers();
builder.Services.AddHostedService<FakeSimulationService>();


var app = builder.Build();

// Serve static files (wwwroot) and default document (index.html)
app.UseDefaultFiles();
app.UseStaticFiles();

// Map the SignalR hub
app.MapHub<SimulationHub>("/simulationHub");
app.MapControllers();

app.Run();
