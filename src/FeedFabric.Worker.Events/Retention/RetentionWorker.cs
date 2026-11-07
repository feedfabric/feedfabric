using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using FeedFabric.Application.Services.Retention;

namespace FeedFabric.Worker.Events.Retention;

// Runs RetentionService every Retention:Interval. Several workers can run; a session advisory lock lets only one
// of them do each run.
public class RetentionWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<RetentionOptions> options,
    TimeProvider timeProvider,
    ILogger<RetentionWorker> logger) : BackgroundService
{
    private const long RetentionLock = 0x4645_4544_5245; // "FEEDRE"

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogWarning("Retention is off (Retention:Enabled=false): nothing is ever deleted");
            return;
        }

        // Not straight away, so a restart loop doesn't run it every time
        await Task.Delay(TimeSpan.FromMinutes(1), timeProvider, stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "The retention job failed; it runs again in {Interval}", options.Value.Interval);
            }
            await Task.Delay(options.Value.Interval, timeProvider, stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // The lock belongs to the connection, so keep one open for the whole run
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            if (!await db.Database.SqlQuery<bool>($"""SELECT pg_try_advisory_lock({RetentionLock}) AS "Value" """).SingleAsync(cancellationToken))
            {
                return;
            }
            try
            {
                await scope.ServiceProvider.GetRequiredService<RetentionService>().RunAsync(cancellationToken);
            }
            finally
            {
                await db.Database.SqlQuery<bool>($"""SELECT pg_advisory_unlock({RetentionLock}) AS "Value" """).SingleAsync(CancellationToken.None);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
