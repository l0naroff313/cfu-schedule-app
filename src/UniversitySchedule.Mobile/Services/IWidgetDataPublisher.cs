namespace UniversitySchedule.Mobile.Services;

public interface IWidgetDataPublisher
{
    Task PublishAsync(CancellationToken cancellationToken = default);
}

public sealed class NoopWidgetDataPublisher : IWidgetDataPublisher
{
    public Task PublishAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
