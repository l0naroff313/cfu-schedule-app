namespace UniversitySchedule.Domain.PersonalData;

public enum PersonalDataEntityKind
{
    Note = 0,
    Assignment = 1,
}

public enum PersonalDataMutationOutcome
{
    Applied = 0,
    RejectedAsStale = 1,
}

public enum PersonalDataMutationApplyResult
{
    Applied = 0,
    AlreadyApplied = 1,
    RejectedAsStale = 2,
}

public sealed class PersonalDataMutationReceipt
{
    private PersonalDataMutationReceipt()
    {
    }

    private PersonalDataMutationReceipt(
        Guid installationId,
        Guid mutationId,
        PersonalDataEntityKind entityKind,
        Guid entityId,
        PersonalDataMutationOutcome outcome,
        DateTimeOffset processedAtUtc)
    {
        InstallationId = installationId;
        MutationId = mutationId;
        EntityKind = entityKind;
        EntityId = entityId;
        Outcome = outcome;
        ProcessedAtUtc = processedAtUtc.ToUniversalTime();
    }

    public Guid InstallationId { get; private set; }

    public Guid MutationId { get; private set; }

    public PersonalDataEntityKind EntityKind { get; private set; }

    public Guid EntityId { get; private set; }

    public PersonalDataMutationOutcome Outcome { get; private set; }

    public DateTimeOffset ProcessedAtUtc { get; private set; }

    public static PersonalDataMutationReceipt Create(
        Guid installationId,
        Guid mutationId,
        PersonalDataEntityKind entityKind,
        Guid entityId,
        PersonalDataMutationOutcome outcome,
        DateTimeOffset processedAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(installationId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(mutationId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(entityId, Guid.Empty);
        if (!Enum.IsDefined(entityKind))
        {
            throw new ArgumentOutOfRangeException(nameof(entityKind));
        }

        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        return new PersonalDataMutationReceipt(
            installationId,
            mutationId,
            entityKind,
            entityId,
            outcome,
            processedAtUtc);
    }
}
