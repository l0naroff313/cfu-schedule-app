#if VISUAL_SNAPSHOTS
using UniversitySchedule.Contracts.Schedule;
using UniversitySchedule.Mobile.Core.Assignments;
using UniversitySchedule.Mobile.Core.Cfu;
using UniversitySchedule.Mobile.Core.Notes;
using UniversitySchedule.Mobile.Core.Profiles;

namespace UniversitySchedule.Mobile.Services;

public sealed class VisualSnapshotService(
    AcademicProfileStore profileStore,
    PersonalNoteStore noteStore,
    PersonalAssignmentStore assignmentStore,
    TimeProvider timeProvider)
{
    private const string GroupCode = "ПИ-б-о-252";
    private const string InstituteName = "Физико-технический институт";
    private const string DirectionName = "Программная инженерия";

    public Task PrepareProfileAsync(CancellationToken cancellationToken = default)
    {
        var profile = new AcademicProfile(
            CfuStableId.Create("institute", InstituteName),
            InstituteName,
            CfuStableId.Create("direction", InstituteName, DirectionName),
            DirectionName,
            CfuStableId.Create("group", GroupCode),
            GroupCode,
            CourseNumber: 2,
            CfuStableId.Create("subgroup", GroupCode, "1"),
            "1 подгруппа");
        return profileStore.SaveAsync(profile, cancellationToken);
    }

    public async Task SeedPersonalDataAsync(
        ScheduleSnapshot? snapshot,
        CancellationToken cancellationToken = default)
    {
        ScheduleLesson[] lessons = snapshot?.Lessons
            .OrderBy(lesson => lesson.StartsAtUtc)
            .ThenBy(lesson => lesson.PairNumber)
            .ToArray() ?? [];
        Guid? FirstLessonId(int index) => lessons.ElementAtOrDefault(index)?.Id;
        string Subject(int index, string fallback) =>
            lessons.ElementAtOrDefault(index)?.Subject ?? fallback;

        DateTimeOffset now = timeProvider.GetUtcNow();
        PersonalNote[] notes =
        [
            new(
                Id("note", 1),
                FirstLessonId(0),
                "Повторить топологии сетей и модель OSI перед следующей практикой.",
                now.AddDays(-3),
                now.AddMinutes(-15),
                "Модель OSI и топологии",
                Subject(0, "Компьютерные сети"),
                true),
            new(
                Id("note", 2),
                FirstLessonId(2),
                "Шаблоны проектирования: Singleton, Factory, Observer и Strategy. Подготовить примеры на C#.",
                now.AddDays(-2),
                now.AddHours(-5),
                "Паттерны проектирования",
                Subject(2, "Объектно-ориентированное программирование"),
                true),
            new(
                Id("note", 3),
                FirstLessonId(1),
                "Формулы условной вероятности, Байеса и математического ожидания.",
                now.AddDays(-1),
                now.AddHours(-8),
                "Формулы к статистике",
                Subject(1, "Теория вероятностей"),
                false),
        ];
        foreach (PersonalNote note in notes)
        {
            await noteStore.ReplaceFromSynchronizationAsync(note.Id, note, cancellationToken);
        }

        PersonalAssignment[] assignments =
        [
            Assignment(1, FirstLessonId(0), Subject(0, "Компьютерные сети"),
                "Подготовить схему локальной сети", now.AddHours(7), PersonalAssignmentStatus.InProgress),
            Assignment(2, FirstLessonId(1), Subject(1, "Теория вероятностей"),
                "Решить задачи 1–4", now.AddHours(14), PersonalAssignmentStatus.New),
            Assignment(3, FirstLessonId(2), Subject(2, "ООП"),
                "Лабораторная работа №4", now.AddDays(2), PersonalAssignmentStatus.New),
            Assignment(4, FirstLessonId(3), Subject(3, "Информационная безопасность"),
                "Подготовить сообщение", now.AddDays(4), PersonalAssignmentStatus.Completed),
            Assignment(5, FirstLessonId(4), Subject(4, "Программирование"),
                "Оформить отчёт по практике", now.AddDays(-1), PersonalAssignmentStatus.Completed),
            Assignment(6, null, "Английский язык",
                "Повторить профессиональную лексику", null, PersonalAssignmentStatus.Completed),
        ];
        foreach (PersonalAssignment assignment in assignments)
        {
            await assignmentStore.ReplaceFromSynchronizationAsync(
                assignment.Id,
                assignment,
                cancellationToken);
        }
    }

    internal async Task MarkReadyAsync(
        VisualSnapshotOptions options,
        CancellationToken cancellationToken = default)
    {
        string markerPath = Path.Combine(FileSystem.Current.CacheDirectory, options.MarkerFileName);
        if (File.Exists(markerPath))
        {
            File.Delete(markerPath);
        }

        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        await File.WriteAllTextAsync(markerPath, DateTimeOffset.UtcNow.ToString("O"), cancellationToken);
    }

    private PersonalAssignment Assignment(
        int index,
        Guid? lessonId,
        string subject,
        string text,
        DateTimeOffset? deadlineUtc,
        PersonalAssignmentStatus status)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        return new PersonalAssignment(
            Id("assignment", index),
            lessonId,
            subject,
            text,
            deadlineUtc,
            status,
            now.AddDays(-index),
            now.AddHours(-index));
    }

    private static Guid Id(string entity, int index) =>
        CfuStableId.Create("visual-snapshot", entity, index.ToString());
}
#endif
