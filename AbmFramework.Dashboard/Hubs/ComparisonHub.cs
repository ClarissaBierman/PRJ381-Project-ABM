using AbmFramework.Config;
using AbmFramework.Dashboard.Comparison;
using AbmFramework.Dashboard.Models;
using AbmFramework.Engine;
using Microsoft.AspNetCore.SignalR;

namespace AbmFramework.Dashboard.Hubs;

// Separate from SimulationHub on purpose: Compare runs in the background
// without the live animation, so it neither touches the live run nor needs
// changes to the shared hub.
public class ComparisonHub : Hub
{
    private readonly ComparisonService _comparison;

    public ComparisonHub(ComparisonService comparison)
    {
        _comparison = comparison;
    }

    // The models the Compare panel can offer.
    public IReadOnlyList<ModelOption> GetModels() => ModelCatalog.Options;

    // The model's default settings, to fill in the Compare panel.
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

    // Runs the batch and returns the summary. Sends "ComparisonProgress"
    // to the caller after every finished run.
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

    public void CancelComparison() => _comparison.Cancel(Context.ConnectionId);

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _comparison.Cancel(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
