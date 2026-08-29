using System.Globalization;

namespace UniversitySchedule.Contracts.Catalog;

public sealed record TeacherIdentity(
    string Key,
    string Surname,
    char? FirstInitial,
    char? MiddleInitial)
{
    public string ScheduleDisplayName
    {
        get
        {
            string initials = string.Concat(
                FirstInitial is char first ? $"{char.ToUpper(first, CultureInfo.CurrentCulture)}." : string.Empty,
                MiddleInitial is char middle ? $"{char.ToUpper(middle, CultureInfo.CurrentCulture)}." : string.Empty);
            return string.IsNullOrEmpty(initials) ? Surname : $"{Surname} {initials}";
        }
    }
}

public static class TeacherIdentityParser
{
    public static bool TryParse(string? displayName, out TeacherIdentity identity)
    {
        identity = new TeacherIdentity(string.Empty, string.Empty, null, null);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return false;
        }

        string[] parts = displayName
            .Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        string surname = NormalizeWord(parts[0]);
        if (surname.Length == 0)
        {
            return false;
        }

        char? firstInitial = null;
        char? middleInitial = null;
        if (parts.Length >= 2)
        {
            string firstNameOrInitials = LettersOnly(parts[1]);
            if (firstNameOrInitials.Length > 1 && parts[1].IndexOf('.') < 0)
            {
                firstInitial = firstNameOrInitials[0];
                if (parts.Length >= 3)
                {
                    string middleName = LettersOnly(parts[2]);
                    middleInitial = middleName.Length == 0 ? null : middleName[0];
                }
            }
            else
            {
                string initials = LettersOnly(string.Concat(parts.Skip(1)));
                firstInitial = initials.Length > 0 ? initials[0] : null;
                middleInitial = initials.Length > 1 ? initials[1] : null;
            }
        }

        string key = string.Join('|',
            surname.ToLowerInvariant().Replace('ё', 'е'),
            firstInitial?.ToString().ToLowerInvariant().Replace('ё', 'е') ?? string.Empty,
            middleInitial?.ToString().ToLowerInvariant().Replace('ё', 'е') ?? string.Empty);
        identity = new TeacherIdentity(key, surname, firstInitial, middleInitial);
        return true;
    }

    private static string NormalizeWord(string value)
    {
        return new string(value
            .Trim()
            .Where(character => char.IsLetter(character) || character is '-' or '\'')
            .ToArray());
    }

    private static string LettersOnly(string value)
    {
        return new string(value.Where(char.IsLetter).ToArray());
    }
}
