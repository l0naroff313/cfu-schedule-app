using System.Security.Cryptography;

namespace UniversitySchedule.Domain.Identity;

public sealed class Installation
{
    private Installation()
    {
    }

    private Installation(
        Guid id,
        byte[] secretHash,
        string platform,
        string appVersion,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        SecretHash = secretHash.ToArray();
        Platform = platform;
        AppVersion = appVersion;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        LastSeenAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; private set; }

    public byte[] SecretHash { get; private set; } = [];

    public string Platform { get; private set; } = string.Empty;

    public string AppVersion { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset LastSeenAtUtc { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public bool IsRevoked => RevokedAtUtc.HasValue;

    public static Installation Register(
        Guid id,
        byte[] secretHash,
        string platform,
        string appVersion,
        DateTimeOffset createdAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentNullException.ThrowIfNull(secretHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(appVersion);

        if (secretHash.Length != 32)
        {
            throw new ArgumentException("The installation secret hash must contain 32 bytes.", nameof(secretHash));
        }

        return new Installation(id, secretHash, platform, appVersion, createdAtUtc);
    }

    public bool MatchesSecretHash(ReadOnlySpan<byte> candidateHash)
    {
        return candidateHash.Length == SecretHash.Length &&
            CryptographicOperations.FixedTimeEquals(candidateHash, SecretHash);
    }

    public void RecordSeen(string platform, string appVersion, DateTimeOffset seenAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(appVersion);

        Platform = platform;
        AppVersion = appVersion;
        DateTimeOffset normalizedSeenAt = seenAtUtc.ToUniversalTime();
        if (normalizedSeenAt > LastSeenAtUtc)
        {
            LastSeenAtUtc = normalizedSeenAt;
        }
    }

    public void Revoke(DateTimeOffset revokedAtUtc)
    {
        RevokedAtUtc ??= revokedAtUtc.ToUniversalTime();
    }
}
