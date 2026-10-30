// =============================================================================
// ComparisonHub.cs  (AbmFramework.Dashboard.Hubs)
//
// The SignalR hub for the Compare panel (Monte Carlo batches). The page calls
// RunComparison once per setting (A, then B), and gets "ComparisonProgress"
// messages while it runs so the progress bar can move.
// =============================================================================

using AbmFramework.Config;
using AbmFramework.Dashboard.Comparison;
using AbmFramework.Dashboard.Models;
using AbmFramework.Engine;
using Microsoft.AspNetCore.SignalR;

namespace AbmFramework.Dashboard.Hubs;

// Separate from SimulationHub on purpose: Compare runs in the background
// without the live animation, so it neither touches the live run nor needs
// changes to the shared hub.
/// <summary>The Compare panel's connection to the server.</summary>
public class ComparisonHub : Hub
{
    private readonly ComparisonService _comparison;

    public ComparisonHub(ComparisonService comparison)
    {
        _comparison = comparison;
    }

    /// <summary>The models the Compare panel can offer.</summary>
    public IReadOnlyList<ModelOption> GetModels() => ModelCatalog.Options;

    /// <summary>The model's default settings, to fill in the Compare panel.</summary>
    public SimulationConfig GetDefaults(string model)
    {
        try
        {
            return _comparison.GetDefaults(model);
        }
        catch (ComparisonRequestException ex)
        {
            throw new HubException(ex.Message);
        }
    }

    /// <summary>
    /// Runs the batch and returns the summary. Sends "ComparisonProgress"
    /// to the caller after every finished run. Only one comparison per page
    /// connection can run at a time.
    /// </summary>
    public async Task<ComparisonSummary> RunComparison(ComparisonRequest request)
    {
        var connectionId = Context.ConnectionId;
        var caller = Clients.Caller;

        var cts = _comparison.TryBegin(connectionId)
                  ?? throw new HubException("A comparison is already running.");

        try
        {
            // Link the page's cancel button / disconnect with the request.
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, Context.ConnectionAborted);

            // "_ =" fires the message without waiting for it, so progress
            // reporting never slows the batch down.
            void Report(int completed) => _ = caller.SendAsync("ComparisonProgress", new ComparisonProgress
            {
                Label = request.Label,
                Completed = completed,
                Total = request.Runs
            });

            // Validates first, so a bad request fails before any progress.
            ComparisonService.BuildConfig(request);
            Report(0);

            return await _comparison.RunAsync(request, new CallbackProgress<int>(Report), linked.Token);
        }
        catch (ComparisonRequestException ex)
        {
            throw new HubException(ex.Message);
        }
        catch (OperationCanceledException)
        {
            throw new HubException("Comparison cancelled.");
        }
        finally
        {
            _comparison.End(connectionId);
        }
    }

    /// <summary>The Cancel button: stops this page's running comparison.</summary>
    public void CancelComparison() => _comparison.Cancel(Context.ConnectionId);

    /// <summary>If the page is closed mid-comparison, stop the batch so it doesn't run for nobody.</summary>
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _comparison.Cancel(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
