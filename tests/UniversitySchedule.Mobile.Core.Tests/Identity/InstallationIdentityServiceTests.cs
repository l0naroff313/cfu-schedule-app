using UniversitySchedule.Mobile.Core.Identity;

namespace UniversitySchedule.Mobile.Core.Tests.Identity;

public sealed class InstallationIdentityServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 30, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetOrCreate_CreatesRandomCredentialsAndPersistsThem()
    {
        var storage = new InMemorySecureValueStore();
        var service = new InstallationIdentityService(storage, new FixedTimeProvider(Now));

        InstallationIdentity identity = await service.GetOrCreateAsync();

        Assert.NotEqual(Guid.Empty, identity.Id);
        Assert.Equal(32, Convert.FromBase64String(identity.Secret).Length);
        Assert.Equal(Now, identity.CreatedAtUtc);
        Assert.Equal(3, storage.Values.Count);
        Assert.Contains(identity.DisplayId, storage.Values.Values);
        Assert.Contains(identity.Secret, storage.Values.Values);
    }

    [Fact]
    public async Task GetOrCreate_ReturnsSameIdentityAcrossServiceInstances()
    {
        var storage = new InMemorySecureValueStore();
        InstallationIdentity first = await new InstallationIdentityService(
            storage,
            new FixedTimeProvider(Now)).GetOrCreateAsync();

        InstallationIdentity second = await new InstallationIdentityService(
            storage,
            new FixedTimeProvider(Now.AddDays(1))).GetOrCreateAsync();

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task GetOrCreate_IsStableDuringConcurrentCalls()
    {
        var service = new InstallationIdentityService(
            new InMemorySecureValueStore(),
            new FixedTimeProvider(Now));

        InstallationIdentity[] identities = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(_ => service.GetOrCreateAsync()));

        Assert.Single(identities.Select(identity => identity.Id).Distinct());
        Assert.Single(identities.Select(identity => identity.Secret).Distinct());
    }

    [Fact]
    public async Task GetOrCreate_ReplacesIncompleteCredentialSet()
    {
        var storage = new InMemorySecureValueStore();
        await storage.SetAsync("cfu.installation.id.v1", Guid.NewGuid().ToString("D"));
        await storage.SetAsync("cfu.installation.secret.v1", "not-a-secret");
        var service = new InstallationIdentityService(storage, new FixedTimeProvider(Now));

        InstallationIdentity identity = await service.GetOrCreateAsync();

        Assert.Equal(32, Convert.FromBase64String(identity.Secret).Length);
        Assert.Equal(3, storage.Values.Count);
        Assert.Contains(identity.DisplayId, storage.Values.Values);
    }

    private sealed class InMemorySecureValueStore : ISecureValueStore
    {
        public Dictionary<string, string> Values { get; } = [];

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(Values.GetValueOrDefault(key));

        public Task SetAsync(
            string key,
            string value,
            CancellationToken cancellationToken = default)
        {
            Values[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            Values.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset current) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => current;
    }
}
