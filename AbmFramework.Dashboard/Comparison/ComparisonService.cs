using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using AbmFramework.Config;
using AbmFramework.Engine;

namespace AbmFramework.Dashboard.Comparison;

// Runs a batch of simulations for the Compare button: builds the config from
// the model's defaults plus the user's settings, validates it, runs it with
// MonteCarloRunner (no animation, no tick delay) and summarises the result.
public class ComparisonService
{
    public const int MinRuns = 2;
    public const int MaxRuns = 100;
    public const int MinGridSize = 10;
    public const int MaxGridSize = 100;
    public const int MaxTickLimit = 5000;

    // Rough ceiling on runs x ticks x agents. The engine manages roughly
    // 300,000 to 500,000 agent-steps per second, so this keeps the worst
    // case to about a minute. Anything bigger is refused up front with a
    // message rather than left to tie up the server during the Expo demo.
    public const long MaxWork = 25_000_000;

    // Settings the user may not change through Compare: the model and seed
    // are set by the request itself.
    private static readonly HashSet<string> LockedSettings = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(SimulationConfig.Model),
        nameof(SimulationConfig.RandomSeed),
        nameof(SimulationConfig.ScenarioName)
    };

    private static readonly Dictionary<string, PropertyInfo> SettableProperties = typeof(SimulationConfig)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanWrite && !LockedSettings.Contains(p.Name))
        .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

    // Two comparisons at once is plenty for a demo laptop; more wait their turn.
    private readonly SemaphoreSlim _slots = new(2, 2);

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new();

    // The model's default settings, used to fill in the Compare panel.
    public SimulationConfig GetDefaults(string model) => ModelCatalog.BuildConfig(ParseModel(model));

    // Marks a connection as running a comparison; null if it already is.
    public CancellationTokenSource? TryBegin(string connectionId)
    {
        var cts = new CancellationTokenSource();
        if (_running.TryAdd(connectionId, cts)) return cts;

        cts.Dispose();
        return null;
    }

    public void End(string connectionId)
    {
        if (_running.TryRemove(connectionId, out var cts)) cts.Dispose();
    }

    public void Cancel(string connectionId)
    {
        if (_running.TryGetValue(connectionId, out var cts))
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
        }
    }

    public async Task<ComparisonSummary> RunAsync(
        ComparisonRequest request,
        IProgress<int> progress,
        CancellationToken cancellationToken)
    {
        var (model, config) = BuildConfig(request);
        int baseSeed = request.Seed ?? Random.Shared.Next();

        await _slots.WaitAsync(cancellationToken);
        try
        {
            // Off the request thread: the runs are CPU-bound.
            var results = await Task.Run(
                () => new MonteCarloRunner().RunAsync(config, request.Runs, baseSeed, cancellationToken, progress),
                cancellationToken);

            return ComparisonSummary.Build(model, baseSeed, results);
        }
        finally
        {
            _slots.Release();
        }
    }

    // Starts from the model's defaults, applies the user's settings and
    // checks the result. Anything the user can fix throws a
    // ComparisonRequestException with a readable message.
    public static (ModelType Model, SimulationConfig Config) BuildConfig(ComparisonRequest request)
    {
        var model = ParseModel(request.Model);

        if (request.Runs < MinRuns || request.Runs > MaxRuns)
            throw new ComparisonRequestException($"Number of runs must be between {MinRuns} and {MaxRuns}.");

        var config = ModelCatalog.BuildConfig(model);
        var errors = new List<string>();

        foreach (var (name, value) in request.Settings)
        {
            if (!SettableProperties.TryGetValue(name, out var property))
            {
                errors.Add($"Unknown setting '{name}'.");
                continue;
            }

            try
            {
                property.SetValue(config, ReadValue(property.PropertyType, value));
            }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException or ArgumentException or OverflowException)
            {
                errors.Add($"'{name}' is not a valid value.");
            }
        }

        if (errors.Count > 0)
            throw new ComparisonRequestException(string.Join(" ", errors));

        if (config.GridWidth < MinGridSize || config.GridWidth > MaxGridSize ||
            config.GridHeight < MinGridSize || config.GridHeight > MaxGridSize)
        {
            throw new ComparisonRequestException(
                $"Grid width and height must each be between {MinGridSize} and {MaxGridSize}.");
        }

        if (config.TickLimit > MaxTickLimit)
            throw new ComparisonRequestException($"Run length can be at most {MaxTickLimit} ticks.");

        // Wolf-Sheep is sized by its two starting populations, and animals
        // may share a cell, so AgentCount is only there to satisfy
        // validation and must not fail a perfectly good small grid.
        if (model == ModelType.WolfSheep)
        {
            config.AgentCount = Math.Clamp(
                config.InitialSheep + config.InitialWolves,
                1,
                Math.Max(1, config.GridWidth * config.GridHeight));
        }

        try
        {
            ConfigLoader.Validate(config);
        }
        catch (ConfigValidationException ex)
        {
            throw new ComparisonRequestException(string.Join(" ", ex.Errors));
        }

        long agents = model == ModelType.WolfSheep ? config.InitialSheep + config.InitialWolves : config.AgentCount;
        long workPerRun = Math.Max(1, (long)config.TickLimit * agents);
        if (request.Runs * workPerRun > MaxWork)
        {
            long fits = MaxWork / workPerRun;
            throw new ComparisonRequestException(fits >= MinRuns
                ? $"That comparison is too large to run quickly. With these settings you can run it up to {fits} times, or use fewer ticks or agents."
                : "That comparison is too large to run quickly. Use fewer ticks or fewer agents.");
        }

        return (model, config);
    }

    private static ModelType ParseModel(string model)
    {
        if (!Enum.TryParse<ModelType>(model, ignoreCase: true, out var parsed))
            throw new ComparisonRequestException($"Unknown model '{model}'.");

        return parsed;
    }

    // Reads a JSON value into a SimulationConfig property type. The page's
    // number boxes always send numbers, so whole-number settings accept 20.0
    // and round anything else rather than rejecting it.
    private static object? ReadValue(Type type, JsonElement value)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;

        if (target == typeof(int))
            return value.TryGetInt32(out var i) ? i : (int)Math.Round(value.GetDouble());

        if (target == typeof(double))
            return value.GetDouble();

        if (target.IsEnum)
        {
            return value.ValueKind == JsonValueKind.String
                ? Enum.Parse(target, value.GetString()!, ignoreCase: true)
                : Enum.ToObject(target, value.GetInt32());
        }

        throw new InvalidOperationException($"Unsupported setting type {target.Name}.");
    }
}

// IProgress<T> that runs the callback straight away on the reporting thread.
// The built-in Progress<T> posts to a SynchronizationContext, which can
// reorder updates; the page already ignores out-of-order counts, but there
// is no reason to introduce the delay.
public sealed class CallbackProgress<T> : IProgress<T>
{
    private readonly Action<T> _callback;

    public CallbackProgress(Action<T> callback) => _callback = callback;

    public void Report(T value) => _callback(value);
}
