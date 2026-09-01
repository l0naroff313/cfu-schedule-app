using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using UniversitySchedule.Contracts.PersonalData;
using UniversitySchedule.Mobile.Core.Assignments;
using UniversitySchedule.Mobile.Core.Notes;

namespace UniversitySchedule.Mobile.Core.Sync;

public sealed record PersonalDataConflictItem(
    Guid MutationId,
    PersonalDataSyncEntityKind EntityKind,
    Guid EntityId,
    string KindText,
    string Title,
    string LocalSummary,
    string LocalTimestampText,
    string ServerSummary,
    string ServerTimestampText,
    bool CanKeepServer);

public sealed class PersonalDataConflictResolutionService(
    PersonalDataSyncQueue queue,
    PersonalNoteStore noteStore,
    PersonalAssignmentStore assignmentStore,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly SemaphoreSlim _resolutionLock = new(1, 1);

    public async Task<IReadOnlyList<PersonalDataConflictItem>> GetConflictsAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PersonalDataSyncOperation> operations = await queue.GetPendingAsync(cancellationToken);
        PersonalDataSyncOperation[] conflicts = operations
            .Where(operation => operation.State == PersonalDataSyncOperationState.Conflict)
            .OrderByDescending(operation => operation.LastAttemptAtUtc ?? operation.OccurredAtUtc)
            .ToArray();
        if (conflicts.Length == 0)
        {
            return [];
        }

        Dictionary<Guid, PersonalNote> notes = (await noteStore.GetAllAsync(cancellationToken))
            .ToDictionary(note => note.Id);
        Dictionary<Guid, PersonalAssignment> assignments =
            (await assignmentStore.GetAllAsync(cancellationToken)).ToDictionary(assignment => assignment.Id);
        return conflicts
            .Select(operation => CreateItem(operation, notes, assignments))
            .ToArray();
    }

    public async Task KeepLocalAsync(
        Guid mutationId,
        CancellationToken cancellationToken = default)
    {
        await _resolutionLock.WaitAsync(cancellationToken);
        try
        {
            PersonalDataSyncOperation conflict = await GetConflictAsync(mutationId, cancellationToken);
            DateTimeOffset resolvedAtUtc = ResolveLocalTimestamp(conflict);
            PersonalDataSyncOperation replacement;
            switch (conflict.EntityKind)
            {
                case PersonalDataSyncEntityKind.Note:
                    PersonalNote? note = await noteStore.GetAsync(conflict.EntityId, cancellationToken) ?? conflict.Note;
                    if (note is null)
                    {
                        replacement = CreateDeleteReplacement(conflict, resolvedAtUtc);
                    }
                    else
                    {
                        PersonalNote resolvedNote = note with { UpdatedAtUtc = resolvedAtUtc };
                        await noteStore.ReplaceFromSynchronizationAsync(
                            conflict.EntityId,
                            resolvedNote,
                            cancellationToken);
                        replacement = CreateUpsertReplacement(conflict, resolvedAtUtc, note: resolvedNote);
                    }

                    break;
                case PersonalDataSyncEntityKind.Assignment:
                    PersonalAssignment? assignment =
                        await assignmentStore.GetAsync(conflict.EntityId, cancellationToken) ?? conflict.Assignment;
                    if (assignment is null)
                    {
                        replacement = CreateDeleteReplacement(conflict, resolvedAtUtc);
                    }
                    else
                    {
                        PersonalAssignment resolvedAssignment = assignment with { UpdatedAtUtc = resolvedAtUtc };
                        await assignmentStore.ReplaceFromSynchronizationAsync(
                            conflict.EntityId,
                            resolvedAssignment,
                            cancellationToken);
                        replacement = CreateUpsertReplacement(
                            conflict,
                            resolvedAtUtc,
                            assignment: resolvedAssignment);
                    }

                    break;
                default:
                    throw new InvalidOperationException($"Unsupported sync entity: {conflict.EntityKind}.");
            }

            await queue.ReplaceEntityOperationsAsync(replacement, cancellationToken);
        }
        finally
        {
            _resolutionLock.Release();
        }
    }

    public async Task KeepServerAsync(
        Guid mutationId,
        CancellationToken cancellationToken = default)
    {
        await _resolutionLock.WaitAsync(cancellationToken);
        try
        {
            PersonalDataSyncOperation conflict = await GetConflictAsync(mutationId, cancellationToken);
            switch (conflict.EntityKind)
            {
                case PersonalDataSyncEntityKind.Note:
                    if (!TryReadServerNote(conflict, out SyncedNoteResponse? note))
                    {
                        throw new InvalidOperationException("The server note version is unavailable.");
                    }

                    await noteStore.ReplaceFromSynchronizationAsync(
                        conflict.EntityId,
                        note.DeletedAtUtc.HasValue ? null : ToPersonalNote(note),
                        cancellationToken);
                    break;
                case PersonalDataSyncEntityKind.Assignment:
                    if (!TryReadServerAssignment(conflict, out SyncedAssignmentResponse? assignment))
                    {
                        throw new InvalidOperationException("The server assignment version is unavailable.");
                    }

                    await assignmentStore.ReplaceFromSynchronizationAsync(
                        conflict.EntityId,
                        assignment.DeletedAtUtc.HasValue ? null : ToPersonalAssignment(assignment),
                        cancellationToken);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported sync entity: {conflict.EntityKind}.");
            }

            await queue.DiscardEntityOperationsAsync(
                conflict.EntityKind,
                conflict.EntityId,
                cancellationToken);
        }
        finally
        {
            _resolutionLock.Release();
        }
    }

    private async Task<PersonalDataSyncOperation> GetConflictAsync(
        Guid mutationId,
        CancellationToken cancellationToken)
    {
        PersonalDataSyncOperation? operation = (await queue.GetPendingAsync(cancellationToken))
            .SingleOrDefault(item => item.MutationId == mutationId);
        if (operation is null)
        {
            throw new KeyNotFoundException($"Sync mutation '{mutationId:D}' was not found.");
        }

        if (operation.State != PersonalDataSyncOperationState.Conflict)
        {
            throw new InvalidOperationException("Only a conflicted sync operation can be resolved.");
        }

        return operation;
    }

    private DateTimeOffset ResolveLocalTimestamp(PersonalDataSyncOperation conflict)
    {
        DateTimeOffset resolvedAtUtc = timeProvider.GetUtcNow().ToUniversalTime();
        DateTimeOffset newestKnownUtc = conflict.OccurredAtUtc.ToUniversalTime();
        if (TryGetServerTimestamp(conflict, out DateTimeOffset serverTimestampUtc) &&
            serverTimestampUtc > newestKnownUtc)
        {
            newestKnownUtc = serverTimestampUtc;
        }

        return resolvedAtUtc > newestKnownUtc ? resolvedAtUtc : newestKnownUtc.AddTicks(1);
    }

    private static PersonalDataSyncOperation CreateDeleteReplacement(
        PersonalDataSyncOperation conflict,
        DateTimeOffset resolvedAtUtc) =>
        new(
            Guid.NewGuid(),
            conflict.EntityKind,
            PersonalDataSyncMutationKind.Delete,
            conflict.EntityId,
            resolvedAtUtc);

    private static PersonalDataSyncOperation CreateUpsertReplacement(
        PersonalDataSyncOperation conflict,
        DateTimeOffset resolvedAtUtc,
        PersonalNote? note = null,
        PersonalAssignment? assignment = null) =>
        new(
            Guid.NewGuid(),
            conflict.EntityKind,
            PersonalDataSyncMutationKind.Upsert,
            conflict.EntityId,
            resolvedAtUtc,
            note,
            assignment);

    private static PersonalDataConflictItem CreateItem(
        PersonalDataSyncOperation operation,
        IReadOnlyDictionary<Guid, PersonalNote> notes,
        IReadOnlyDictionary<Guid, PersonalAssignment> assignments)
    {
        return operation.EntityKind switch
        {
            PersonalDataSyncEntityKind.Note => CreateNoteItem(operation, notes.GetValueOrDefault(operation.EntityId)),
            PersonalDataSyncEntityKind.Assignment =>
                CreateAssignmentItem(operation, assignments.GetValueOrDefault(operation.EntityId)),
            _ => throw new InvalidOperationException($"Unsupported sync entity: {operation.EntityKind}."),
        };
    }

    private static PersonalDataConflictItem CreateNoteItem(
        PersonalDataSyncOperation operation,
        PersonalNote? storedNote)
    {
        PersonalNote? local = storedNote ?? operation.Note;
        bool hasServer = TryReadServerNote(operation, out SyncedNoteResponse? server);
        string title = FirstNonEmpty(local?.Title, server?.Title, local?.Subject, server?.Subject, "Заметка");
        return new PersonalDataConflictItem(
            operation.MutationId,
            operation.EntityKind,
            operation.EntityId,
            "Заметка",
            title,
            local is null ? "Удалена на устройстве" : CreateNoteSummary(local.Text, local.Subject, local.IsPinned),
            FormatTimestamp(local?.UpdatedAtUtc ?? operation.OccurredAtUtc),
            !hasServer
                ? "Серверная версия недоступна. Можно сохранить локальную версию."
                : server!.DeletedAtUtc.HasValue
                    ? "Удалена на сервере"
                    : CreateNoteSummary(server.Text, server.Subject, server.IsPinned),
            hasServer
                ? FormatTimestamp(server!.DeletedAtUtc ?? server.UpdatedAtUtc)
                : "Нет данных о времени",
            hasServer);
    }

    private static PersonalDataConflictItem CreateAssignmentItem(
        PersonalDataSyncOperation operation,
        PersonalAssignment? storedAssignment)
    {
        PersonalAssignment? local = storedAssignment ?? operation.Assignment;
        bool hasServer = TryReadServerAssignment(operation, out SyncedAssignmentResponse? server);
        string title = FirstNonEmpty(local?.Subject, server?.Subject, "Задание");
        return new PersonalDataConflictItem(
            operation.MutationId,
            operation.EntityKind,
            operation.EntityId,
            "Задание",
            title,
            local is null
                ? "Удалено на устройстве"
                : CreateAssignmentSummary(local.Text, local.Status, local.DeadlineUtc),
            FormatTimestamp(local?.UpdatedAtUtc ?? operation.OccurredAtUtc),
            !hasServer
                ? "Серверная версия недоступна. Можно сохранить локальную версию."
                : server!.DeletedAtUtc.HasValue
                    ? "Удалено на сервере"
                    : CreateAssignmentSummary(
                        server.Text,
                        (PersonalAssignmentStatus)server.Status,
                        server.DeadlineUtc),
            hasServer
                ? FormatTimestamp(server!.DeletedAtUtc ?? server.UpdatedAtUtc)
                : "Нет данных о времени",
            hasServer);
    }

    private static bool TryReadServerNote(
        PersonalDataSyncOperation operation,
        [NotNullWhen(true)] out SyncedNoteResponse? response)
    {
        response = Deserialize<SyncedNoteResponse>(operation.ConflictServerStateJson);
        return response is not null &&
               response.Id == operation.EntityId &&
               response.Revision > 0 &&
               response.UpdatedAtUtc != default &&
               (response.DeletedAtUtc.HasValue || !string.IsNullOrWhiteSpace(response.Text));
    }

    private static bool TryReadServerAssignment(
        PersonalDataSyncOperation operation,
        [NotNullWhen(true)] out SyncedAssignmentResponse? response)
    {
        response = Deserialize<SyncedAssignmentResponse>(operation.ConflictServerStateJson);
        return response is not null &&
               response.Id == operation.EntityId &&
               response.Revision > 0 &&
               response.UpdatedAtUtc != default &&
               Enum.IsDefined(response.Status) &&
               (response.DeletedAtUtc.HasValue || !string.IsNullOrWhiteSpace(response.Text));
    }

    private static bool TryGetServerTimestamp(
        PersonalDataSyncOperation operation,
        out DateTimeOffset timestampUtc)
    {
        if (operation.EntityKind == PersonalDataSyncEntityKind.Note &&
            TryReadServerNote(operation, out SyncedNoteResponse? note))
        {
            timestampUtc = (note.DeletedAtUtc ?? note.UpdatedAtUtc).ToUniversalTime();
            return true;
        }

        if (operation.EntityKind == PersonalDataSyncEntityKind.Assignment &&
            TryReadServerAssignment(operation, out SyncedAssignmentResponse? assignment))
        {
            timestampUtc = (assignment.DeletedAtUtc ?? assignment.UpdatedAtUtc).ToUniversalTime();
            return true;
        }

        timestampUtc = default;
        return false;
    }

    private static T? Deserialize<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static PersonalNote ToPersonalNote(SyncedNoteResponse response) =>
        new(
            response.Id,
            response.LessonId,
            response.Text,
            response.CreatedAtUtc,
            response.UpdatedAtUtc,
            response.Title,
            response.Subject,
            response.IsPinned);

    private static PersonalAssignment ToPersonalAssignment(SyncedAssignmentResponse response) =>
        new(
            response.Id,
            response.LessonId,
            response.Subject,
            response.Text,
            response.DeadlineUtc,
            (PersonalAssignmentStatus)response.Status,
            response.CreatedAtUtc,
            response.UpdatedAtUtc);

    private static string CreateNoteSummary(string text, string? subject, bool isPinned)
    {
        string metadata = string.Join(
            " • ",
            new[] { subject, isPinned ? "Закреплена" : null }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return JoinSummary(metadata, text);
    }

    private static string CreateAssignmentSummary(
        string text,
        PersonalAssignmentStatus status,
        DateTimeOffset? deadlineUtc)
    {
        string statusText = status switch
        {
            PersonalAssignmentStatus.New => "Новое",
            PersonalAssignmentStatus.InProgress => "В работе",
            PersonalAssignmentStatus.Completed => "Выполнено",
            _ => "Неизвестный статус",
        };
        string metadata = deadlineUtc is DateTimeOffset deadline
            ? $"{statusText} • до {deadline.ToLocalTime():dd.MM.yyyy HH:mm}"
            : $"{statusText} • без дедлайна";
        return JoinSummary(metadata, text);
    }

    private static string JoinSummary(string metadata, string text)
    {
        string preview = NormalizePreview(text);
        return string.IsNullOrWhiteSpace(metadata) ? preview : $"{metadata}\n{preview}";
    }

    private static string NormalizePreview(string value)
    {
        string normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 220 ? normalized : $"{normalized[..217]}…";
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.First(value => !string.IsNullOrWhiteSpace(value))!.Trim();

    private static string FormatTimestamp(DateTimeOffset value) =>
        $"Изменено {value.ToLocalTime():dd.MM.yyyy, HH:mm}";
}
