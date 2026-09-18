using System.Globalization;

namespace UniversitySchedule.Mobile.Core.Cfu;

/// <summary>Reject truncated calendars before expanding recurring lessons or replacing durable data.</summary>
public static class CfuCalendarIntegrity
{
    public const string FallbackWarning = "Календарь недель КФУ недоступен или неполон. Используется сохранённый календарь.";

    public static bool IsUsable(CfuScheduleIndexDocument index)
    {
        if (index.Weeks is null || index.Weeks.EvenWeekMondays is not { Count: > 0 } even ||
            index.Weeks.OddWeekMondays is not { Count: > 0 } odd) return false;

        var dates = new HashSet<DateOnly>();
        foreach (string value in even.Concat(odd))
        {
            if (!TryDate(value, out DateOnly date) || date.DayOfWeek != DayOfWeek.Monday || !dates.Add(date))
                return false;
        }

        DateOnly[] sorted = dates.Order().ToArray();
        for (int i = 1; i < sorted.Length; i++)
            if (sorted[i].DayNumber - sorted[i - 1].DayNumber != 7) return false;

        // The server's current week is a useful completeness check, independent of the device clock.
        return string.IsNullOrWhiteSpace(index.CurrentWeek?.Monday) || index.CurrentWeek.BeforeStart ||
            TryDate(index.CurrentWeek.Monday, out DateOnly current) && dates.Contains(current);
    }

    private static bool TryDate(string value, out DateOnly date) => DateOnly.TryParseExact(
        value, ["yyyy-MM-dd", "dd.MM.yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
