using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;
using UniversitySchedule.Contracts.Catalog;

namespace UniversitySchedule.ScheduleImporter.Sources;

public sealed partial class VuzopediaSourceClient(
    CachedHttpSource source,
    ImportOptions options,
    ILogger<VuzopediaSourceClient> logger)
{
    public const string TeachersUrl = "https://vuzopedia.ru/vuz/5346/teacher";
    public const string SpecialtiesUrl = "https://vuzopedia.ru/vuz/5346/spec";

    private readonly CachedHttpSource _source = source ?? throw new ArgumentNullException(nameof(source));
    private readonly ImportOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly ILogger<VuzopediaSourceClient> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly HtmlParser _parser = new();

    public async Task<IReadOnlyList<VuzopediaProgram>> LoadProgramsAsync(
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, VuzopediaProgram>(StringComparer.OrdinalIgnoreCase);
        int pageCount = 1;

        for (int page = 1; page <= pageCount; page++)
        {
            Uri uri = new(page == 1 ? SpecialtiesUrl : $"{SpecialtiesUrl}?page={page}");
            string content = await _source.GetAsync(uri, "vuzopedia-specialties", true, cancellationToken);
            IDocument document = await _parser.ParseDocumentAsync(content, cancellationToken);
            pageCount = Math.Max(pageCount, FindLastPage(document));

            foreach (IElement card in document.QuerySelectorAll(".blockNewItem[data-entity='napr']"))
            {
                VuzopediaProgram? program = ParseProgramCard(card);
                if (program is not null)
                {
                    result.TryAdd(program.Url, program);
                }
            }

            _logger.LogInformation("Loaded Vuzopedia specialty page {Page}/{Total}", page, pageCount);
        }

        return result.Values
            .OrderBy(program => program.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(program => program.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<VuzopediaTeacherProfile>> LoadTeachersAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<VuzopediaTeacherListItem> list = await LoadTeacherListAsync(cancellationToken);
        if (_options.SkipTeacherDetails)
        {
            var cachedProfiles = new List<VuzopediaTeacherProfile>(list.Count);
            foreach (VuzopediaTeacherListItem item in list)
            {
                string? content = await _source.TryGetCachedAsync(
                    new Uri(item.Url),
                    "vuzopedia-teacher-details",
                    cancellationToken);
                cachedProfiles.Add(content is null
                    ? new VuzopediaTeacherProfile(item.FullName, null, [], [], item.Url)
                    : await ParseTeacherProfileAsync(item, content, cancellationToken));
            }

            return cachedProfiles;
        }

        var result = new List<VuzopediaTeacherProfile>(list.Count);
        for (int index = 0; index < list.Count; index++)
        {
            VuzopediaTeacherListItem item = list[index];
            string content = await _source.GetAsync(
                new Uri(item.Url),
                "vuzopedia-teacher-details",
                true,
                cancellationToken);
            result.Add(await ParseTeacherProfileAsync(item, content, cancellationToken));

            int completed = index + 1;
            if (completed % 25 == 0 || completed == list.Count)
            {
                _logger.LogInformation("Loaded {Completed}/{Total} Vuzopedia teacher profiles", completed, list.Count);
            }
        }

        return result;
    }

    internal async Task<IReadOnlyList<VuzopediaTeacherListItem>> LoadTeacherListAsync(
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, VuzopediaTeacherListItem>(StringComparer.OrdinalIgnoreCase);
        int pageCount = 1;

        for (int page = 1; page <= pageCount; page++)
        {
            Uri uri = new(page == 1 ? TeachersUrl : $"{TeachersUrl}?page={page}");
            string content = await _source.GetAsync(uri, "vuzopedia-teacher-list", true, cancellationToken);
            IDocument document = await _parser.ParseDocumentAsync(content, cancellationToken);
            pageCount = Math.Max(pageCount, FindLastPage(document));

            foreach (IElement link in document.QuerySelectorAll("article.vxTeacherCard a.vxTeacherCard__name"))
            {
                string name = NormalizeText(link.TextContent);
                string? href = link.GetAttribute("href");
                if (name.Length == 0 || string.IsNullOrWhiteSpace(href))
                {
                    continue;
                }

                string url = new Uri(new Uri("https://vuzopedia.ru"), href).AbsoluteUri;
                result.TryAdd(url, new VuzopediaTeacherListItem(name, url));
            }

            _logger.LogInformation("Loaded Vuzopedia teacher list page {Page}/{Total}", page, pageCount);
        }

        return result.Values
            .OrderBy(item => item.FullName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    internal async Task<VuzopediaTeacherProfile> ParseTeacherProfileAsync(
        VuzopediaTeacherListItem listItem,
        string content,
        CancellationToken cancellationToken)
    {
        IDocument document = await _parser.ParseDocumentAsync(content, cancellationToken);
        string heading = NormalizeText(document.QuerySelector("h1")?.TextContent);
        string fullName = heading.Split(':', 2, StringSplitOptions.TrimEntries)[0];
        if (fullName.Length == 0)
        {
            fullName = listItem.FullName;
        }

        IElement? details = document.QuerySelectorAll("div.itemVuz")
            .FirstOrDefault(element => NormalizeText(element.TextContent).Contains("Должность:", StringComparison.Ordinal));
        string? position = details?.QuerySelectorAll("div")
            .FirstOrDefault(element => element.Children.Any(child =>
                child.LocalName == "b" &&
                NormalizeText(child.TextContent).Equals("Должность:", StringComparison.Ordinal)))?
            .TextContent;
        position = NormalizeText(position)
            .Replace("Должность:", string.Empty, StringComparison.Ordinal)
            .Trim();
        string[] disciplines = details?.QuerySelectorAll("div")
            .FirstOrDefault(element => NormalizeText(element.TextContent)
                .StartsWith("Преподаваемые дисциплины:", StringComparison.Ordinal))?
            .QuerySelectorAll("li")
            .Select(element => NormalizeText(element.TextContent))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToArray() ?? [];
        VuzopediaProgram[] specialties = details?.QuerySelectorAll(".blockNewItem[data-entity='napr']")
            .Select(ParseProgramCard)
            .Where(program => program is not null)
            .Cast<VuzopediaProgram>()
            .GroupBy(program => program.Url, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray() ?? [];

        return new VuzopediaTeacherProfile(
            fullName,
            NormalizeOptional(position),
            disciplines,
            specialties,
            listItem.Url);
    }

    internal static VuzopediaProgram? ParseProgramCard(IElement card)
    {
        IElement? link = card.QuerySelector("a.newItemSpPrTitle");
        string name = NormalizeText(link?.TextContent);
        string information = NormalizeText(card.QuerySelector(".osnBlockInfoSm")?.TextContent);
        string? href = link?.GetAttribute("href");
        Match match = ProgramInformationRegex().Match(information);
        if (name.Length == 0 || string.IsNullOrWhiteSpace(href) || !match.Success)
        {
            return null;
        }

        string url = new Uri(new Uri("https://vuzopedia.ru"), href).AbsoluteUri;
        return new VuzopediaProgram(
            match.Groups["code"].Value,
            name,
            ParseEducationLevel(match.Groups["level"].Value),
            ParseStudyForms(information),
            url);
    }

    internal static int FindLastPage(IDocument document)
    {
        return document.QuerySelectorAll("a[href*='?page=']")
            .Select(link => link.GetAttribute("href"))
            .Where(href => !string.IsNullOrWhiteSpace(href))
            .Select(href => PageNumberRegex().Match(href!))
            .Where(match => match.Success && int.TryParse(match.Groups["page"].Value, out _))
            .Select(match => int.Parse(match.Groups["page"].Value, System.Globalization.CultureInfo.InvariantCulture))
            .DefaultIfEmpty(1)
            .Max();
    }

    private static EducationLevel ParseEducationLevel(string value)
    {
        return NormalizeText(value).ToLowerInvariant() switch
        {
            "бакалавриат" => EducationLevel.Bachelor,
            "специалитет" => EducationLevel.Specialist,
            "магистратура" => EducationLevel.Master,
            string level when level.Contains("аспирант", StringComparison.Ordinal) => EducationLevel.Postgraduate,
            _ => EducationLevel.Unknown,
        };
    }

    private static IReadOnlyList<StudyForm> ParseStudyForms(string value)
    {
        return StudyFormRegex().Matches(NormalizeText(value))
            .Select(match => match.Value.ToLowerInvariant() switch
            {
                "очная" => StudyForm.FullTime,
                "очно-заочная" => StudyForm.PartTime,
                "заочная" => StudyForm.Extramural,
                _ => StudyForm.Unknown,
            })
            .Where(form => form != StudyForm.Unknown)
            .Distinct()
            .OrderBy(form => form)
            .ToArray();
    }

    private static string NormalizeText(string? value)
    {
        return string.Join(' ', (value ?? string.Empty)
            .Replace('\u00a0', ' ')
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string? NormalizeOptional(string? value)
    {
        string normalized = NormalizeText(value);
        return normalized.Length == 0 ? null : normalized;
    }

    [GeneratedRegex(@"(?<code>\d+(?:\.\d+){2})\s+(?<level>Бакалавриат|Специалитет|Магистратура|Подготовка[^|]*)", RegexOptions.IgnoreCase)]
    private static partial Regex ProgramInformationRegex();

    [GeneratedRegex(@"Очно-заочная|Очная|Заочная", RegexOptions.IgnoreCase)]
    private static partial Regex StudyFormRegex();

    [GeneratedRegex(@"[?&]page=(?<page>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex PageNumberRegex();
}
