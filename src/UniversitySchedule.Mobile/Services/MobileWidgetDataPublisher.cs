using System.Text.Json;
using UniversitySchedule.Mobile.Core.Notes;
using UniversitySchedule.Mobile.Core.Profiles;
using UniversitySchedule.Mobile.Core.Scheduling;
using UniversitySchedule.Mobile.Core.Widgets;

namespace UniversitySchedule.Mobile.Services;

public sealed class MobileWidgetDataPublisher : IWidgetDataPublisher, IDisposable
{
    private readonly ScheduleSession _scheduleSession;
    private readonly AcademicProfileStore _profileStore;
    private readonly PersonalNoteStore _noteStore;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public MobileWidgetDataPublisher(
        ScheduleSession scheduleSession,
        AcademicProfileStore profileStore,
        PersonalNoteStore noteStore,
        TimeProvider timeProvider)
    {
        _scheduleSession = scheduleSession;
        _profileStore = profileStore;
        _noteStore = noteStore;
        _timeProvider = timeProvider;
        _scheduleSession.Changed += OnChanged;
        _noteStore.Changed += OnChanged;
    }

    public async Task PublishAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            AcademicProfile? profile = await _profileStore.GetAsync(cancellationToken);
            IReadOnlyList<PersonalNote> notes = await _noteStore.GetAllAsync(cancellationToken);
            ScheduleWidgetState state = ScheduleWidgetStateBuilder.Build(
                _scheduleSession.Snapshot, profile, notes, _timeProvider.GetUtcNow());
            await WriteAsync(state, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        _scheduleSession.Changed -= OnChanged;
        _noteStore.Changed -= OnChanged;
        _lock.Dispose();
    }

    private void OnChanged(object? sender, EventArgs args) => _ = PublishSafelyAsync();

    private async Task PublishSafelyAsync()
    {
        try { await PublishAsync(); }
        catch (Exception) { /* Widget refresh must never affect the main app. */ }
    }

    private static Task WriteAsync(ScheduleWidgetState state, CancellationToken cancellationToken)
    {
#if ANDROID
        return AndroidWidgetBridge.WriteAsync(JsonSerializer.Serialize(state), cancellationToken);
#else
        return Task.CompletedTask;
#endif
    }
}
