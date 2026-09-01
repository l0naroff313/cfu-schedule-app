using System.Globalization;
using System.Security.Cryptography;

namespace UniversitySchedule.Mobile.Core.Identity;

public sealed class InstallationIdentityService(
    ISecureValueStore secureValueStore,
    TimeProvider timeProvider)
{
    private const string IdKey = "cfu.installation.id.v1";
    private const string SecretKey = "cfu.installation.secret.v1";
    private const string CreatedAtKey = "cfu.installation.created-at.v1";
    private const int SecretLength = 32;

    private readonly ISecureValueStore _secureValueStore = secureValueStore
        ?? throw new ArgumentNullException(nameof(secureValueStore));
    private readonly TimeProvider _timeProvider = timeProvider
        ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly SemaphoreSlim _lock = new(1, 1);
    private InstallationIdentity? _cachedIdentity;

    public async Task<InstallationIdentity> GetOrCreateAsync(
        CancellationToken cancellationToken = default)
    {
        if (_cachedIdentity is not null)
        {
            return _cachedIdentity;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedIdentity is not null)
            {
                return _cachedIdentity;
            }

            InstallationIdentity? stored = await LoadAsync(cancellationToken);
            if (stored is not null)
            {
                _cachedIdentity = stored;
                return stored;
            }

            await ClearIncompleteStateAsync(cancellationToken);
            var created = new InstallationIdentity(
                Guid.NewGuid(),
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretLength)),
                _timeProvider.GetUtcNow().ToUniversalTime());

            // The identifier is written last and acts as the commit marker for the credential set.
            await _secureValueStore.SetAsync(SecretKey, created.Secret, cancellationToken);
            await _secureValueStore.SetAsync(
                CreatedAtKey,
                created.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                cancellationToken);
            await _secureValueStore.SetAsync(IdKey, created.DisplayId, cancellationToken);

            _cachedIdentity = created;
            return created;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<InstallationIdentity?> LoadAsync(CancellationToken cancellationToken)
    {
        string? idValue = await _secureValueStore.GetAsync(IdKey, cancellationToken);
        string? secretValue = await _secureValueStore.GetAsync(SecretKey, cancellationToken);
        string? createdAtValue = await _secureValueStore.GetAsync(CreatedAtKey, cancellationToken);

        if (!Guid.TryParseExact(idValue, "D", out Guid id) ||
            id == Guid.Empty ||
            !HasValidSecret(secretValue) ||
            !DateTimeOffset.TryParseExact(
                createdAtValue,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset createdAt))
        {
            return null;
        }

        return new InstallationIdentity(id, secretValue!, createdAt.ToUniversalTime());
    }

    private async Task ClearIncompleteStateAsync(CancellationToken cancellationToken)
    {
        await _secureValueStore.RemoveAsync(IdKey, cancellationToken);
        await _secureValueStore.RemoveAsync(SecretKey, cancellationToken);
        await _secureValueStore.RemoveAsync(CreatedAtKey, cancellationToken);
    }

    private static bool HasValidSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            return Convert.FromBase64String(value).Length == SecretLength;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
