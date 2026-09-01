using UniversitySchedule.Domain.PersonalData;

namespace UniversitySchedule.Domain.Tests.PersonalData;

public sealed class SyncedNoteTests
{
    [Fact]
    public void Apply_DuplicateMutationIsAcknowledgedWithoutSecondRevision()
    {
        DateTimeOffset createdAt = new(2026, 8, 30, 10, 0, 0, TimeSpan.Zero);
        Guid mutationId = Guid.NewGuid();
        SyncedNote note = SyncedNote.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            mutationId,
            null,
            "Первая версия",
            null,
            null,
            false,
            createdAt,
            createdAt,
            createdAt);

        PersonalDataMutationApplyResult result = note.Apply(
            mutationId,
            null,
            "Повторная отправка",
            null,
            null,
            false,
            createdAt,
            createdAt.AddMinutes(1),
            createdAt.AddMinutes(1));

        Assert.Equal(PersonalDataMutationApplyResult.AlreadyApplied, result);
        Assert.Equal("Первая версия", note.Text);
        Assert.Equal(1, note.Revision);
    }

    [Fact]
    public void Apply_StaleMutationReturnsConflictAndPreservesCurrentVersion()
    {
        DateTimeOffset createdAt = new(2026, 8, 30, 10, 0, 0, TimeSpan.Zero);
        SyncedNote note = SyncedNote.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            "Новая версия",
            null,
            null,
            false,
            createdAt,
            createdAt.AddMinutes(2),
            createdAt.AddMinutes(2));

        PersonalDataMutationApplyResult result = note.Apply(
            Guid.NewGuid(),
            null,
            "Старая версия",
            null,
            null,
            false,
            createdAt,
            createdAt.AddMinutes(1),
            createdAt.AddMinutes(3));

        Assert.Equal(PersonalDataMutationApplyResult.RejectedAsStale, result);
        Assert.Equal("Новая версия", note.Text);
        Assert.Equal(1, note.Revision);
    }
}
