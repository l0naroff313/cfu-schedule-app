using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Text.Json.Serialization;
using UniversitySchedule.ScheduleImporter.Sources;

namespace UniversitySchedule.ScheduleImporter;

/// <summary>
/// Reads the CFU timetable template exported as XLSX. The workbook is a visual grid:
/// every weekday occupies a 40-row block and each lesson uses three rows (subject,
/// teacher, classroom). This parser converts that layout into the same parity-based
/// lesson document used by the mobile client.
/// </summary>
public sealed partial class ExcelScheduleImporter
{
    private static readonly string[] DefaultEvenMondays =
        ["2026-09-07", "2026-09-21", "2026-10-05", "2026-10-19", "2026-11-02", "2026-11-16", "2026-11-30", "2026-12-14"];
    private static readonly string[] DefaultOddMondays =
        ["2026-09-14", "2026-09-28", "2026-10-12", "2026-10-26", "2026-11-09", "2026-11-23", "2026-12-07", "2026-12-21"];

    public ManualScheduleOverrideDocument Parse(string inputPath, int academicYear = 2026)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        if (!File.Exists(inputPath) && !Directory.Exists(inputPath))
        {
            throw new FileNotFoundException("Excel schedule source was not found.", inputPath);
        }

        string? temporaryDirectory = null;
        try
        {
            string[] workbooks;
            if (Directory.Exists(inputPath))
            {
                workbooks = Directory.GetFiles(inputPath, "*.xlsx", SearchOption.AllDirectories);
            }
            else if (string.Equals(Path.GetExtension(inputPath), ".zip", StringComparison.OrdinalIgnoreCase))
            {
                temporaryDirectory = Directory.CreateTempSubdirectory("cfu-excel-").FullName;
                ZipFile.ExtractToDirectory(inputPath, temporaryDirectory);
                workbooks = Directory.GetFiles(temporaryDirectory, "*.xlsx", SearchOption.AllDirectories);
            }
            else
            {
                workbooks = [inputPath];
            }

            if (workbooks.Length == 0)
            {
                throw new InvalidDataException("The Excel archive does not contain any .xlsx workbooks.");
            }

            var evenMondays = new HashSet<string>(StringComparer.Ordinal);
            var oddMondays = new HashSet<string>(StringComparer.Ordinal);
            var groups = new Dictionary<string, Dictionary<string, CfuLessonDocument>>(StringComparer.OrdinalIgnoreCase);

            foreach (string workbookPath in workbooks.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                foreach (SheetGrid sheet in XlsxReader.Read(workbookPath))
                {
                    ParseSheet(sheet, academicYear, evenMondays, oddMondays, groups);
                }
            }

            if (groups.Count == 0)
            {
                throw new InvalidDataException("The Excel source contains no recognizable group lessons.");
            }

            string[] even = evenMondays.Count == 0 ? DefaultEvenMondays : evenMondays.OrderBy(value => value).ToArray();
            string[] odd = oddMondays.Count == 0 ? DefaultOddMondays : oddMondays.OrderBy(value => value).ToArray();
            return new ManualScheduleOverrideDocument
            {
                SourceFile = Path.GetFileName(inputPath),
                ImportedAtUtc = DateTimeOffset.UtcNow,
                Bells = DefaultBells,
                Weeks = new CfuWeeksDocument { EvenWeekMondays = even, OddWeekMondays = odd },
                Groups = groups
                    .OrderBy(item => item.Key, StringComparer.CurrentCultureIgnoreCase)
                    .Select(item => new CfuGroupScheduleDocument
                    {
                        Code = item.Key,
                        Lessons = item.Value.Values
                            .OrderBy(lesson => lesson.Parity, StringComparer.OrdinalIgnoreCase)
                            .ThenBy(lesson => lesson.Day)
                            .ThenBy(lesson => lesson.PairNumber)
                            .ThenBy(lesson => lesson.Subgroup)
                            .ThenBy(lesson => lesson.Subject, StringComparer.CurrentCultureIgnoreCase)
                            .ToArray(),
                    })
                    .ToArray(),
            };
        }
        finally
        {
            if (temporaryDirectory is not null)
            {
                try
                {
                    Directory.Delete(temporaryDirectory, recursive: true);
                }
                catch (IOException)
                {
                    // A locked temporary workbook must not hide a successful import.
                }
            }
        }
    }

    private static readonly IReadOnlyList<CfuBellDocument> DefaultBells =
    [
        new() { PairNumber = 1, StartsAt = "08:00", EndsAt = "09:30" },
        new() { PairNumber = 2, StartsAt = "09:50", EndsAt = "11:20" },
        new() { PairNumber = 3, StartsAt = "11:30", EndsAt = "13:00" },
        new() { PairNumber = 4, StartsAt = "13:20", EndsAt = "14:50" },
        new() { PairNumber = 5, StartsAt = "15:00", EndsAt = "16:30" },
        new() { PairNumber = 6, StartsAt = "16:40", EndsAt = "18:10" },
        new() { PairNumber = 7, StartsAt = "18:20", EndsAt = "19:50" },
        new() { PairNumber = 8, StartsAt = "20:00", EndsAt = "21:30" },
    ];

    private static void ParseSheet(
        SheetGrid sheet,
        int academicYear,
        ISet<string> evenMondays,
        ISet<string> oddMondays,
        IDictionary<string, Dictionary<string, CfuLessonDocument>> groups)
    {
        IReadOnlyList<int> blockStarts = Enumerable.Range(0, sheet.MaxColumn + 1)
            .Where(column => IsDayHeader(sheet.Get(2, column)))
            .ToArray();
        if (blockStarts.Count == 0)
        {
            return;
        }

        for (int blockIndex = 0; blockIndex < blockStarts.Count; blockIndex++)
        {
            int startColumn = blockStarts[blockIndex];
            int endColumn = blockIndex + 1 < blockStarts.Count
                ? blockStarts[blockIndex + 1]
                : sheet.MaxColumn + 1;
            string parity = ResolveParity(sheet, startColumn, endColumn);
            IReadOnlyList<string> mondays = ParseMondays(
                string.Join(' ', Enumerable.Range(startColumn, Math.Max(0, endColumn - startColumn))
                    .Select(column => sheet.Get(0, column))),
                academicYear);
            foreach (string monday in mondays)
            {
                (parity == "нечетная" ? oddMondays : evenMondays).Add(monday);
            }

            GroupColumn[] columns = ReadGroupColumns(sheet, startColumn, endColumn);
            if (columns.Length == 0)
            {
                continue;
            }

            int? currentDay = null;
            for (int row = 4; row <= sheet.MaxRow; row++)
            {
                if (TryParseDay(sheet.Get(row, startColumn), out int day))
                {
                    currentDay = day;
                }

                if (currentDay is null || !int.TryParse(sheet.Get(row, startColumn + 1), out int pair) || pair is < 1 or > 8)
                {
                    continue;
                }

                foreach (GroupColumn column in columns)
                {
                    string subject = CleanText(sheet.Get(row, column.Column));
                    if (subject.Length == 0)
                    {
                        continue;
                    }

                    string teacher = NormalizeTeacher(sheet.Get(row + 1, column.Column));
                    (string? classroom, string? building) = SplitRoom(sheet.Get(row + 2, column.Column));
                    string? lessonType = NormalizeOptional(sheet.Get(row, column.TypeColumn));
                    var lesson = new CfuLessonDocument
                    {
                        GroupCode = column.GroupCode,
                        Subgroup = column.Subgroup,
                        Day = currentDay.Value,
                        PairNumber = pair,
                        Parity = parity,
                        Subject = subject,
                        LessonType = lessonType,
                        Teachers = teacher.Length == 0 ? [] : [teacher],
                        Classroom = classroom,
                        Building = building,
                    };

                    if (!groups.TryGetValue(column.GroupCode, out Dictionary<string, CfuLessonDocument>? lessons))
                    {
                        lessons = new Dictionary<string, CfuLessonDocument>(StringComparer.OrdinalIgnoreCase);
                        groups.Add(column.GroupCode, lessons);
                    }

                    lessons[LessonKey(lesson)] = lesson;
                }
            }
        }
    }

    private static GroupColumn[] ReadGroupColumns(SheetGrid sheet, int startColumn, int endColumn)
    {
        var result = new List<GroupColumn>();
        for (int column = startColumn + 3; column < endColumn; column++)
        {
            string header = CleanText(sheet.Get(3, column));
            if (header.Length == 0)
            {
                header = CleanText(sheet.Get(2, column));
            }

            if (!TryParseGroupHeader(header, out string groupCode, out int subgroup))
            {
                continue;
            }

            int typeColumn = column - 1;
            while (typeColumn >= startColumn && !IsLessonTypeHeader(sheet.Get(2, typeColumn)))
            {
                typeColumn--;
            }

            result.Add(new GroupColumn(groupCode, subgroup, column, typeColumn >= startColumn ? typeColumn : column - 1));
        }

        return result
            .GroupBy(column => $"{column.GroupCode}|{column.Subgroup}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static bool TryParseGroupHeader(string value, out string groupCode, out int subgroup)
    {
        groupCode = string.Empty;
        subgroup = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        Match match = GroupHeaderRegex().Match(value.Trim());
        if (match.Success && int.TryParse(match.Groups["subgroup"].Value, out int parsedSubgroup))
        {
            groupCode = NormalizeGroupCode(match.Groups["group"].Value);
            subgroup = parsedSubgroup;
            return groupCode.Length > 0;
        }

        if (!value.Contains('-', StringComparison.Ordinal))
        {
            return false;
        }

        groupCode = NormalizeGroupCode(value);
        return groupCode.Length > 0;
    }

    private static string ResolveParity(SheetGrid sheet, int startColumn, int endColumn)
    {
        string header = string.Join(' ', Enumerable.Range(startColumn, Math.Max(0, endColumn - startColumn))
            .Select(column => CleanText(sheet.Get(0, column))))
            .ToLowerInvariant()
            .Replace('ё', 'е');
        return header.Contains("нечет", StringComparison.Ordinal) ? "нечетная" : "четная";
    }

    private static IReadOnlyList<string> ParseMondays(string text, int academicYear)
    {
        var values = new List<string>();
        foreach (Match match in DateRegex().Matches(text))
        {
            string[] parts = match.Value.Split('.');
            if (parts.Length == 3 && int.TryParse(parts[2], out int year))
            {
                values.Add($"{year:0000}-{int.Parse(parts[1]):00}-{int.Parse(parts[0]):00}");
            }
            else if (parts.Length == 2 && int.TryParse(parts[0], out int day) && int.TryParse(parts[1], out int month))
            {
                values.Add($"{academicYear:0000}-{month:00}-{day:00}");
            }
        }

        return values.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static bool IsDayHeader(string value) =>
        string.Equals(CleanText(value), "дни недели", StringComparison.CurrentCultureIgnoreCase);

    private static bool IsLessonTypeHeader(string value) =>
        string.Equals(CleanText(value), "вид занятий", StringComparison.CurrentCultureIgnoreCase);

    private static bool TryParseDay(string value, out int day)
    {
        day = CleanText(value).ToLowerInvariant() switch
        {
            "понедельник" => 1,
            "вторник" => 2,
            "среда" => 3,
            "четверг" => 4,
            "пятница" => 5,
            "суббота" => 6,
            "воскресенье" => 7,
            _ => 0,
        };
        return day > 0;
    }

    private static string NormalizeGroupCode(string value) =>
        string.Concat(CleanText(value).Where(character => !char.IsWhiteSpace(character)));

    private static string NormalizeTeacher(string value)
    {
        string clean = CleanText(value);
        clean = TeacherPrefixRegex().Replace(clean, string.Empty).Trim();
        return clean;
    }

    private static (string? Classroom, string? Building) SplitRoom(string value)
    {
        string clean = CleanText(value);
        if (clean.Length == 0)
        {
            return (null, null);
        }

        Match match = RoomRegex().Match(clean);
        if (!match.Success)
        {
            return (clean, null);
        }

        string room = match.Groups["room"].Value.Trim();
        string building = match.Groups["building"].Value.Trim();
        if (room.StartsWith("ауд.", StringComparison.CurrentCultureIgnoreCase))
        {
            room = room[4..].Trim();
        }

        return (NormalizeOptional(room), NormalizeOptional(building));
    }

    private static string? NormalizeOptional(string value)
    {
        string clean = CleanText(value);
        return clean.Length == 0 ? null : clean;
    }

    private static string CleanText(string? value) => string.Join(' ',
        (value ?? string.Empty).Replace('\u00a0', ' ')
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string LessonKey(CfuLessonDocument lesson) => string.Join('|',
        lesson.GroupCode,
        lesson.Subgroup.ToString(CultureInfo.InvariantCulture),
        lesson.Day.ToString(CultureInfo.InvariantCulture),
        lesson.PairNumber.ToString(CultureInfo.InvariantCulture),
        lesson.Parity,
        lesson.Subject,
        lesson.LessonType,
        string.Join(',', lesson.Teachers),
        lesson.Classroom,
        lesson.Building);

    [GeneratedRegex(@"^(?<group>.+?)\s*\((?<subgroup>[12])\)$")]
    private static partial Regex GroupHeaderRegex();

    [GeneratedRegex(@"\d{1,2}\.\d{1,2}(?:\.\d{4})?")]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"^(?:(?:ст\.\s*пр\.|доц\.|проф\.|пр\.|асс\.|зав\.\s*каф\.)\s*)+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TeacherPrefixRegex();

    [GeneratedRegex(@"^(?:ауд\.\s*)?(?<room>[^()]+?)(?:\((?<building>[^()]*)\))?$")]
    private static partial Regex RoomRegex();

    private sealed record GroupColumn(string GroupCode, int Subgroup, int Column, int TypeColumn);

    private sealed class SheetGrid
    {
        private readonly IReadOnlyDictionary<int, IReadOnlyDictionary<int, string>> _rows;

        public SheetGrid(IReadOnlyDictionary<int, IReadOnlyDictionary<int, string>> rows, int maxRow, int maxColumn)
        {
            _rows = rows;
            MaxRow = maxRow;
            MaxColumn = maxColumn;
        }

        public int MaxRow { get; }
        public int MaxColumn { get; }

        public string Get(int row, int column) =>
            _rows.TryGetValue(row, out IReadOnlyDictionary<int, string>? values) &&
            values.TryGetValue(column, out string? value)
                ? value
                : string.Empty;
    }

    private static class XlsxReader
    {
        private static readonly XNamespace SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace PackageRelationshipNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";

        public static IReadOnlyList<SheetGrid> Read(string path)
        {
            using ZipArchive archive = ZipFile.OpenRead(path);
            Dictionary<string, string> sharedStrings = ReadSharedStrings(archive);
            XDocument workbook = LoadXml(archive, "xl/workbook.xml");
            XDocument relationships = LoadXml(archive, "xl/_rels/workbook.xml.rels");
            Dictionary<string, string> targets = relationships.Root?.Elements(PackageRelationshipNamespace + "Relationship")
                .Where(element => element.Attribute("Id") is not null && element.Attribute("Target") is not null)
                .ToDictionary(
                    element => element.Attribute("Id")!.Value,
                    element => ResolveTarget(element.Attribute("Target")!.Value),
                    StringComparer.Ordinal)
                ?? new Dictionary<string, string>(StringComparer.Ordinal);

            var sheets = new List<SheetGrid>();
            foreach (XElement sheet in workbook.Root?.Element(SpreadsheetNamespace + "sheets")?.Elements(SpreadsheetNamespace + "sheet") ?? [])
            {
                string? relationshipId = sheet.Attribute(RelationshipNamespace + "id")?.Value;
                if (relationshipId is null || !targets.TryGetValue(relationshipId, out string? target))
                {
                    continue;
                }

                XDocument worksheet = LoadXml(archive, target);
                sheets.Add(ReadSheet(worksheet, sharedStrings));
            }

            return sheets;
        }

        private static SheetGrid ReadSheet(XDocument document, IReadOnlyDictionary<string, string> sharedStrings)
        {
            var rows = new Dictionary<int, IReadOnlyDictionary<int, string>>();
            int maxRow = 0;
            int maxColumn = 0;
            foreach (XElement row in document.Root?.Element(SpreadsheetNamespace + "sheetData")?.Elements(SpreadsheetNamespace + "row") ?? [])
            {
                if (!int.TryParse(row.Attribute("r")?.Value, out int rowNumber))
                {
                    continue;
                }

                var values = new Dictionary<int, string>();
                foreach (XElement cell in row.Elements(SpreadsheetNamespace + "c"))
                {
                    string? address = cell.Attribute("r")?.Value;
                    if (address is null || !TryParseColumn(address, out int column))
                    {
                        continue;
                    }

                    string value = ReadCell(cell, sharedStrings);
                    if (value.Length > 0)
                    {
                        values[column] = value;
                        maxColumn = Math.Max(maxColumn, column);
                    }
                }

                rows[rowNumber - 1] = values;
                maxRow = Math.Max(maxRow, rowNumber - 1);
            }

            return new SheetGrid(rows, maxRow, maxColumn);
        }

        private static Dictionary<string, string> ReadSharedStrings(ZipArchive archive)
        {
            ZipArchiveEntry? entry = archive.GetEntry("xl/sharedStrings.xml");
            if (entry is null)
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            using Stream stream = entry.Open();
            XDocument document = XDocument.Load(stream);
            return document.Root?.Elements(SpreadsheetNamespace + "si")
                .Select((item, index) => new { index, value = string.Concat(item.Descendants(SpreadsheetNamespace + "t").Select(text => text.Value)) })
                .ToDictionary(item => item.index.ToString(CultureInfo.InvariantCulture), item => item.value, StringComparer.Ordinal)
                ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }

        private static string ReadCell(XElement cell, IReadOnlyDictionary<string, string> sharedStrings)
        {
            string type = cell.Attribute("t")?.Value ?? string.Empty;
            if (string.Equals(type, "inlineStr", StringComparison.Ordinal))
            {
                return string.Concat(cell.Descendants(SpreadsheetNamespace + "t").Select(text => text.Value));
            }

            string value = cell.Element(SpreadsheetNamespace + "v")?.Value ?? string.Empty;
            if (string.Equals(type, "s", StringComparison.Ordinal) && sharedStrings.TryGetValue(value, out string? shared))
            {
                return shared;
            }

            return value;
        }

        private static bool TryParseColumn(string address, out int column)
        {
            column = 0;
            int index = 0;
            while (index < address.Length && char.IsLetter(address[index]))
            {
                column = column * 26 + char.ToUpperInvariant(address[index]) - 'A' + 1;
                index++;
            }

            if (index == 0)
            {
                return false;
            }

            column--;
            return true;
        }

        private static XDocument LoadXml(ZipArchive archive, string path)
        {
            string normalized = path.TrimStart('/').Replace('\\', '/');
            ZipArchiveEntry? entry = archive.GetEntry(normalized);
            if (entry is null)
            {
                throw new InvalidDataException($"The XLSX archive is missing {normalized}.");
            }

            using Stream stream = entry.Open();
            return XDocument.Load(stream);
        }

        private static string ResolveTarget(string target)
        {
            string normalized = target.TrimStart('/').Replace('\\', '/');
            return normalized.StartsWith("xl/", StringComparison.Ordinal)
                ? normalized
                : $"xl/{normalized}";
        }
    }
}

public sealed class ManualScheduleOverrideDocument
{
    [JsonPropertyName("sourceFile")]
    public string SourceFile { get; init; } = string.Empty;

    [JsonPropertyName("importedAtUtc")]
    public DateTimeOffset ImportedAtUtc { get; init; }

    [JsonPropertyName("bells")]
    public IReadOnlyList<CfuBellDocument> Bells { get; init; } = [];

    [JsonPropertyName("weeks")]
    public CfuWeeksDocument Weeks { get; init; } = new();

    [JsonPropertyName("tree")]
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>> Tree { get; init; }
        = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>>();

    [JsonPropertyName("groups")]
    public IReadOnlyList<CfuGroupScheduleDocument> Groups { get; init; } = [];
}
