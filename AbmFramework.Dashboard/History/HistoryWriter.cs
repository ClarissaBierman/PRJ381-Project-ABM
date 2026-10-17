using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using ABM.Persistence;

namespace AbmFramework.Dashboard.History;

// Owns the database and does every write on one background task, in the
// order the writes were queued, so saving never slows a simulation down.
public sealed class HistoryWriter : IDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ILogger<HistoryWriter> _logger;
    private readonly Channel<Func<IRepository, Task>> _queue = Channel.CreateUnbounded<Func<IRepository, Task>>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly Task? _worker;

    public IRepository? Repository { get; }

    public string DatabasePath { get; }

    // Why saving is off, if it is.
    public string? Problem { get; }

    public bool Enabled => Repository is not null;

    public HistoryWriter(IConfiguration configuration, ILogger<HistoryWriter> logger)
    {
        _logger = logger;
        DatabasePath = configuration["Database:Path"] is { Length: > 0 } configured
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PRJ381-ABM", "abm.db");

        try
        {
            Repository = new SimulationRepository(DatabasePath);
            _logger.LogInformation("Saving runs to {Path}", Path.GetFullPath(DatabasePath));
            _worker = Task.Run(WorkAsync);

            // A run that was still going when the dashboard was closed can't be resumed.
            Enqueue(repository => repository.CloseUnfinishedRunsAsync("Stopped"));
        }
        catch (Exception ex)
        {
            Problem = $"The database could not be opened at {DatabasePath}: {ex.Message}";
            _logger.LogError(ex, "Runs will not be saved. The database could not be opened at {Path}", DatabasePath);
        }
    }

    public void Enqueue(Func<IRepository, Task> write)
    {
        if (Enabled)
        {
            _queue.Writer.TryWrite(write);
        }
    }

    // Completes once everything queued before this call has been written.
    public Task WhenIdleAsync()
    {
        if (!Enabled) return Task.CompletedTask;

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_queue.Writer.TryWrite(_ => { done.TrySetResult(); return Task.CompletedTask; }))
        {
            done.TrySetResult();
        }
        return done.Task;
    }

    private async Task WorkAsync()
    {
        await foreach (var write in _queue.Reader.ReadAllAsync())
        {
            try
            {
                await write(Repository!);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not save to the database");
            }
        }
    }

    public void Dispose()
    {
        _queue.Writer.TryComplete();
        try
        {
            _worker?.Wait(TimeSpan.FromSeconds(10));
        }
        catch (AggregateException)
        {
        }
    }
}
