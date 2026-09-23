using System.Text.Json;
using UniversitySchedule.Mobile.Core.Profiles;
using UniversitySchedule.Contracts.Schedule;

namespace UniversitySchedule.Mobile.Core.Cfu;

public sealed partial class CfuScheduleRepository
{
    private const string ElectiveKey = "cfu:electives:v1";

    public async Task<CfuElectiveDocument> LoadElectivesAsync(CancellationToken cancellationToken = default) =>
        (await LoadNetworkFirstAsync<CfuElectiveDocument>(ElectiveKey, "elektiv", ValidateElectives, cancellationToken)).Value;

    private static CfuElectiveDocument ValidateElectives(CfuElectiveDocument document)
    {
        if (document.Groups is not { Count: > 0 } || document.Lessons is null ||
            document.Bells is not { Count: > 0 } || document.Weeks is null ||
            !CfuCalendarIntegrity.IsUsable(new() { Weeks = new() {
                EvenWeekMondays = document.Weeks.Even, OddWeekMondays = document.Weeks.Odd } }))
            throw new InvalidDataException("КФУ вернул неполный каталог элективов.");
        return document;
    }

    public async Task<CfuScheduleLoadResult?> LoadProfileScheduleAsync(AcademicProfile profile,
        bool cachedOnly = false, CancellationToken cancellationToken = default)
    {
        string digits = new((profile.SubgroupName ?? "").Where(char.IsDigit).ToArray());
        int? subgroup = int.TryParse(digits, out int n) && n > 0 ? n : null;
        var main = cachedOnly
            ? await LoadCachedGroupScheduleAsync(profile.GroupName, subgroup, cancellationToken)
            : await LoadGroupScheduleAsync(profile.GroupName, subgroup, cancellationToken);
        if (main is null || string.IsNullOrWhiteSpace(profile.ElectiveGroupCode) ||
            !CfuElectiveDocument.IsEligible(profile.CourseNumber)) return main;

        DocumentLoadResult<CfuElectiveDocument> elective;
        if (cachedOnly)
        {
            var saved = await _localDataStore.GetAsync(ElectiveKey, cancellationToken);
            if (saved is null) throw new InvalidDataException("Офлайн-копия электива ещё не загружена.");
            elective = new(Deserialize<CfuElectiveDocument>(saved.Content, ValidateElectives), saved.UpdatedAtUtc, true);
        }
        else
        {
            elective = await LoadNetworkFirstAsync<CfuElectiveDocument>(ElectiveKey, "elektiv", value => {
                ValidateElectives(value);
                value.Map(profile); // Validate this selection before replacing its durable copy.
                return value;
            }, cancellationToken);
        }

        var extra = elective.Value.Map(profile);
        // The group feed reserves several slots, not just the selected elective's slot.
        // Remove those placeholders before adding actual lessons from the selected group.
        var lessons = main.Snapshot.Lessons.Where(l => !IsElectivePlaceholder(l.Subject))
            .Concat(extra.Lessons).DistinctBy(l => l.Id).OrderBy(l => l.StartsAtUtc).ThenBy(l => l.Subject).ToArray();
        var warnings = new List<string>();
        if (main.Warning is not null) warnings.Add(main.Warning);
        if (elective.Value.Lessons.Any(l => l.GroupCode == profile.ElectiveGroupCode && l.Module.HasValue))
            warnings.Add($"{profile.ElectiveGroupCode}: показан модуль {profile.ElectiveModule}. Даты модулей КФУ не указаны — переключение в учебном профиле.");
        if (extra.Lessons.Count == 0) warnings.Add($"Для {profile.ElectiveGroupCode} КФУ ещё не опубликовал занятия.");
        return main with {
            Snapshot = main.Snapshot with { Lessons = lessons,
                Version = main.Snapshot.Version + ":" + extra.Version,
                From = main.Snapshot.From < extra.From ? main.Snapshot.From : extra.From,
                To = main.Snapshot.To > extra.To ? main.Snapshot.To : extra.To },
            IsFromCache = main.IsFromCache || elective.IsFromCache,
            UpdatedAtUtc = main.UpdatedAtUtc < elective.UpdatedAtUtc ? main.UpdatedAtUtc : elective.UpdatedAtUtc,
            Warning = warnings.Count == 0 ? null : string.Join(" ", warnings)
        };
    }

    private static bool IsElectivePlaceholder(string subject)
    {
        var title = subject.TrimStart();
        return title.StartsWith("Элективная дисциплина", StringComparison.OrdinalIgnoreCase) ||
            title.StartsWith("Элективные дисциплины", StringComparison.OrdinalIgnoreCase);
    }
}
