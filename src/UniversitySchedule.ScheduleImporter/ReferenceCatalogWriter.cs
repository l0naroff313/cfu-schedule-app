using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using UniversitySchedule.Contracts.Catalog;

namespace UniversitySchedule.ScheduleImporter;

public sealed class ReferenceCatalogWriter(ImportOptions options)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly ImportOptions _options = options;

    public async Task WriteAsync(ReferenceCatalogSnapshot snapshot, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_options.OutputPath)!);
        Directory.CreateDirectory(_options.ReportsDirectory);

        await WriteAtomicAsync(
            _options.OutputPath,
            JsonSerializer.Serialize(snapshot, JsonOptions),
            cancellationToken);
        await WriteAtomicAsync(
            Path.Combine(_options.ReportsDirectory, "schedule-coverage.md"),
            BuildCoverageReport(snapshot),
            cancellationToken);
    }

    internal static string BuildCoverageReport(ReferenceCatalogSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Покрытие расписания КФУ");
        builder.AppendLine();
        builder.AppendLine($"Сформировано: {snapshot.GeneratedAtUtc:yyyy-MM-dd HH:mm} UTC.");
        builder.AppendLine();
        builder.AppendLine($"- Подразделений: {snapshot.Statistics.InstituteCount}");
        builder.AppendLine($"- Направлений: {snapshot.Statistics.ProgramCount}");
        builder.AppendLine($"- Групп: {snapshot.Statistics.GroupCount}");
        builder.AppendLine($"- Преподавателей: {snapshot.Statistics.TeacherCount}");
        builder.AppendLine($"- Обогащённых карточек преподавателей: {snapshot.Statistics.EnrichedTeacherProfileCount}");
        builder.AppendLine($"- Преподавателей с расписанием: {snapshot.Statistics.TeachersWithScheduleCount}");
        builder.AppendLine($"- Преподавателей без опубликованного расписания: {snapshot.Statistics.TeachersWithoutScheduleCount}");
        builder.AppendLine($"- Неоднозначных совпадений фамилии и инициалов: {snapshot.Statistics.AmbiguousTeacherMatchesCount}");
        builder.AppendLine($"- Направлений без опубликованного расписания: {snapshot.Statistics.ProgramsWithoutScheduleCount}");
        builder.AppendLine($"- Карточки преподавателей загружены полностью: {(snapshot.Statistics.TeacherDetailsIncluded ? "да" : "нет")}");
        builder.AppendLine();
        builder.AppendLine("## Направления без опубликованного расписания");
        builder.AppendLine();

        AcademicProgramReference[] missing = snapshot.Programs.Where(program => !program.HasPublishedSchedule).ToArray();
        if (missing.Length == 0)
        {
            builder.AppendLine("На момент импорта отсутствующие направления не обнаружены.");
        }
        else
        {
            foreach (IGrouping<string, AcademicProgramReference> institute in missing.GroupBy(program => program.InstituteName))
            {
                builder.AppendLine($"### {institute.Key}");
                builder.AppendLine();
                foreach (AcademicProgramReference program in institute)
                {
                    builder.AppendLine($"- {program.Code} {program.Name}");
                }

                builder.AppendLine();
            }
        }

        builder.AppendLine("## Ограничения сопоставления");
        builder.AppendLine();
        builder.AppendLine("Расписание связывается с преподавателем только при точном совпадении нормализованных фамилии и инициалов. Совпадения с несколькими профилями не назначаются автоматически.");
        builder.AppendLine();
        builder.AppendLine("## Направления только во внешнем каталоге");
        builder.AppendLine();
        if (snapshot.SourceOnlyPrograms.Count == 0)
        {
            builder.AppendLine("Расхождений не обнаружено.");
        }
        else
        {
            foreach (ReferenceCatalogDiscrepancy program in snapshot.SourceOnlyPrograms)
            {
                builder.AppendLine($"- {program.Code} {program.Name} — {program.Source}");
            }
        }

        return builder.ToString();
    }

    private static async Task WriteAtomicAsync(string path, string content, CancellationToken cancellationToken)
    {
        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temporaryPath, content, new UTF8Encoding(false), cancellationToken);
        File.Move(temporaryPath, path, overwrite: true);
    }
}
