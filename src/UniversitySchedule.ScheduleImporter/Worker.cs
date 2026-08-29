namespace UniversitySchedule.ScheduleImporter;

public sealed class Worker(
    ReferenceCatalogBuilder builder,
    ReferenceCatalogWriter writer,
    IHostApplicationLifetime lifetime,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            logger.LogInformation("Reference catalog import started");
            await writer.WriteAsync(await builder.BuildAsync(stoppingToken), stoppingToken);
            logger.LogInformation("Reference catalog import completed");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Reference catalog import cancelled; cached pages remain available for resume");
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "Reference catalog import failed; the previous output was preserved");
            Environment.ExitCode = 1;
        }
        finally
        {
            lifetime.StopApplication();
        }
    }
}
