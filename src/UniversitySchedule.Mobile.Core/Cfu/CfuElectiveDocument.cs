using System.Text.Json.Serialization;
using UniversitySchedule.Contracts.Schedule;
using UniversitySchedule.Mobile.Core.Profiles;

namespace UniversitySchedule.Mobile.Core.Cfu;

public sealed class CfuElectiveDocument
{
    [JsonPropertyName("группы"), JsonRequired]
    public IReadOnlyList<CfuElectiveGroup> Groups { get; init; } = [];
    [JsonPropertyName("занятия"), JsonRequired]
    public IReadOnlyList<CfuLessonDocument> Lessons { get; init; } = [];
    [JsonPropertyName("звонки")]
    public IReadOnlyList<CfuBellDocument> Bells { get; init; } = [];
    [JsonPropertyName("недели")]
    public CfuElectiveWeeks Weeks { get; init; } = new();

    public static bool IsEligible(int course) => course is 2 or 4;

    public ScheduleSnapshot Map(AcademicProfile profile)
    {
        var group = Groups.FirstOrDefault(g => g.Code == profile.ElectiveGroupCode && g.Course == profile.CourseNumber)
            ?? throw new InvalidDataException("Выбранная группа ЦК/ДРПК недоступна для этого курса. Откройте учебный профиль.");
        var rows = Lessons.Where(l => l.GroupCode == group.Code).ToArray();
        if (rows.Any(l => l.Module.HasValue) && !rows.Any(l => l.Module == profile.ElectiveModule))
            throw new InvalidDataException("Выберите модуль ЦК в учебном профиле.");
        var index = new CfuScheduleIndexDocument { Bells = Bells,
            Weeks = new() { EvenWeekMondays = Weeks.Even, OddWeekMondays = Weeks.Odd } };
        if (Bells.Count == 0 || !CfuCalendarIntegrity.IsUsable(index))
            throw new InvalidDataException("Календарь элективов КФУ неполон. Сохранённая копия не изменена.");
        var snapshot = CfuScheduleMapper.MapGroup(index, new() { Code = group.Code,
            Lessons = rows.Where(l => !l.Module.HasValue || l.Module == profile.ElectiveModule)
                .Select(l => new CfuLessonDocument {
                    GroupCode = l.GroupCode, Day = l.Day, PairNumber = l.PairNumber,
                    Date = l.Date, Parity = l.Module.HasValue && string.IsNullOrWhiteSpace(l.Parity) ? "обе" : l.Parity,
                    Subject = l.Subject, LessonType = l.LessonType, Teachers = l.Teachers,
                    Classroom = l.Classroom, Building = l.Building, Online = l.Online,
                    Note = l.Note
                }).ToArray() });
        return snapshot with { Lessons = snapshot.Lessons.Select(l => l with {
            SourceNote = string.Join(" • ", new[] { l.SourceNote, group.Code,
                profile.ElectiveModule.HasValue ? $"Модуль {profile.ElectiveModule} (выбран в профиле)" : null }
                .Where(s => !string.IsNullOrWhiteSpace(s))) }).ToArray() };
    }
}

public sealed class CfuElectiveGroup
{
    [JsonPropertyName("код")] public string Code { get; init; } = "";
    [JsonPropertyName("курс")] public int Course { get; init; }
    [JsonPropertyName("дисциплина")] public string Discipline { get; init; } = "";
    public override string ToString() => Code;
}

public sealed class CfuElectiveWeeks
{
    [JsonPropertyName("чётные")] public IReadOnlyList<string> Even { get; init; } = [];
    [JsonPropertyName("нечётные")] public IReadOnlyList<string> Odd { get; init; } = [];
}
