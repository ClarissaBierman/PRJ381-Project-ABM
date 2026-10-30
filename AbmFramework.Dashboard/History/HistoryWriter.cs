// =============================================================================
// HistoryWriter.cs  (AbmFramework.Dashboard.History)
//
// Owns the database for the dashboard and does every write on ONE background
// task, in the order the writes were queued, so saving never slows the
// simulation down.
//
// How it works: code anywhere calls Enqueue(repository => repository.SaveX(...)).
// That only adds the job to a queue (a Channel) and returns at once. A single
// worker loop (WorkAsync) takes jobs off the queue one by one and runs them.
// One worker means writes never overlap and always happen in order.
//
// Where the database lives:
//   - the "Database:Path" setting if you give one (an appsettings.json file,
//     a Database__Path environment variable or --Database:Path=... when
//     starting the app), otherwise
//   - %LOCALAPPDATA%\PRJ381-ABM\abm.db on Windows
//     (~/.local/share/PRJ381-ABM/abm.db on Linux/Mac).
// If the database can't be opened, the dashboard still works but runs aren't
// saved, and Problem explains why (Past runs shows it).
// =============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using ABM.Persistence;

namespace AbmFramework.Dashboard.History;

/// <summary>
/// Owns the database and does every write on one background task, in the
/// order the writes were queued, so saving never slows a simulation down.
/// </summary>
public sealed class HistoryWriter : IDisposable
{
    /// <summary>
    /// The JSON settings used whenever the dashboard stores JSON in the
    /// database: camelCase names, and enums written as text ("WolfSheep").
    /// </summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ILogger<HistoryWriter> _logger;
    // The job queue. A Channel is a thread-safe queue that the worker can
    // "await" on, so it sleeps until there is something to do.
    private readonly Channel<Func<IRepository, Task>> _queue = Channel.CreateUnbounded<Func<IRepository, Task>>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly Task? _worker;

    /// <summary>
    /// The database. Null if it couldn't be opened. Reading (Past runs) can use
    /// this directly, but writing should go through <see cref="Enqueue"/>.
    /// </summary>
    public IRepository? Repository { get; }

    /// <summary>Where the database file is (or should have been).</summary>
    public string DatabasePath { get; }

    /// <summary>Why saving is off, if it is. Null when everything is fine.</summary>
    public string? Problem { get; }

    /// <summary>True if the database opened and runs are being saved.</summary>
    public bool Enabled => Repository is not null;

    /// <summary>Opens the database and starts the background writer. Created once, at start-up.</summary>
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

    /// <summary>
    /// Queues a database write and returns straight away. The write runs later
    /// on the background worker. Does nothing if saving is off.
    /// Errors inside the write are logged, not thrown.
    /// </summary>
    public void Enqueue(Func<IRepository, Task> write)
    {
        if (Enabled)
        {
            _queue.Writer.TryWrite(write);
        }
    }

    /// <summary>
    /// Completes once everything queued before this call has been written.
    /// Used before reading a run back (Continue) so it is fully saved first.
    /// </summary>
    public Task WhenIdleAsync()
    {
        if (!Enabled) return Task.CompletedTask;

        // Trick: queue a "job" that just completes this task. Because jobs run
        // in order, it completes after every earlier job has finished.
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_queue.Writer.TryWrite(_ => { done.TrySetResult(); return Task.CompletedTask; }))
        {
            done.TrySetResult();
        }
        return done.Task;
    }

    // The background worker: runs queued writes one at a time, forever,
    // until the queue is closed in Dispose().
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

    /// <summary>
    /// Called when the app shuts down: stops accepting new writes and waits
    /// (up to 10 seconds) for the queued ones to finish.
    /// </summary>
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
