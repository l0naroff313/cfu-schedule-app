using UniversitySchedule.Contracts.Catalog;

namespace UniversitySchedule.ScheduleImporter;

public sealed class Worker(
    ReferenceCatalogBuilder builder,
    ReferenceCatalogReader reader,
    ExcelScheduleImporter excelScheduleImporter,
    ExcelScheduleWriter excelScheduleWriter,
    IEnumerable<IReferenceCatalogSink> sinks,
    IEnumerable<IReferenceCatalogFailureSink> failureSinks,
    ImportOptions options,
    IHostApplicationLifetime lifetime,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;
        try
        {
            if (!string.IsNullOrWhiteSpace(options.ExcelPath))
            {
                logger.LogInformation("Excel schedule import started from {Path}", options.ExcelPath);
                ManualScheduleOverrideDocument parsed = excelScheduleImporter.Parse(options.ExcelPath!, options.AcademicYear);
                await excelScheduleWriter.WriteAsync(parsed, stoppingToken);
                logger.LogInformation(
                    "Excel schedule import completed: {Groups} groups, {Lessons} lessons",
                    parsed.Groups.Count,
                    parsed.Groups.Sum(group => group.Lessons.Count));
            }
            else
            {
                logger.LogInformation("Reference catalog import started");
                ReferenceCatalogSnapshot snapshot = options.SeedPostgreSql
                    ? await reader.ReadAsync(stoppingToken)
                    : await builder.BuildAsync(stoppingToken);
                foreach (IReferenceCatalogSink sink in sinks)
                {
                    await sink.WriteAsync(snapshot, stoppingToken);
                }

                logger.LogInformation("Reference catalog import completed");
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Reference catalog import cancelled; cached pages remain available for resume");
        }
        catch (Exception exception)
        {
            foreach (IReferenceCatalogFailureSink sink in failureSinks)
            {
                try
                {
                    await sink.WriteFailureAsync(startedAtUtc, exception, CancellationToken.None);
                }
                catch (Exception auditException)
                {
                    logger.LogError(auditException, "Could not persist the failed import audit record");
                }
            }

            logger.LogCritical(exception, "Reference catalog import failed; the previous output was preserved");
            Environment.ExitCode = 1;
        }
        finally
        {
            lifetime.StopApplication();
        }
    }
}
