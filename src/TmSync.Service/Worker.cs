using TmSync.Core.Config;

namespace TmSync.Service;

/// <summary>Background worker that runs a full sync pass on a fixed interval.</summary>
public sealed class Worker : BackgroundService
{
    private readonly SyncRunner _runner;
    private readonly TmSyncOptions _options;
    private readonly ILogger<Worker> _log;

    public Worker(SyncRunner runner, TmSyncOptions options, ILogger<Worker> log)
    {
        _runner = runner;
        _options = options;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.IntervalMinutes));
        _log.LogInformation("TmSync service started; syncing every {Interval}", interval);

        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await _runner.RunOnceAsync(null, null, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Sync pass failed; will retry on next interval");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));

        _log.LogInformation("TmSync service stopping");
    }
}
