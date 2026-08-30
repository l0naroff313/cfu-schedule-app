using System.Security.Cryptography;
using UniversitySchedule.Application.Identity;
using UniversitySchedule.Domain.Identity;

namespace UniversitySchedule.Application.Tests.Identity;

public sealed class InstallationRegistrationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Register_NewInstallation_PersistsItAndIssuesToken()
    {
        var repository = new FakeRepository();
        InstallationRegistrationService service = CreateService(repository);
        Guid installationId = Guid.NewGuid();

        InstallationRegistrationResult result = await service.RegisterAsync(CreateCommand(installationId));

        Assert.Equal(InstallationRegistrationStatus.Success, result.Status);
        Assert.True(result.IsNewInstallation);
        Assert.Equal("test-token", result.AccessToken?.Value);
        Assert.True(repository.Items.ContainsKey(installationId));
    }

    [Fact]
    public async Task Register_SameSecret_UpdatesLastSeenWithoutAddingDuplicate()
    {
        var repository = new FakeRepository();
        InstallationRegistrationService service = CreateService(repository);
        Guid installationId = Guid.NewGuid();
        InstallationRegistrationCommand command = CreateCommand(installationId);
        await service.RegisterAsync(command);

        InstallationRegistrationResult result = await service.RegisterAsync(command with { AppVersion = "1.2.0" });

        Assert.Equal(InstallationRegistrationStatus.Success, result.Status);
        Assert.False(result.IsNewInstallation);
        Assert.Single(repository.Items);
        Assert.Equal("1.2.0", repository.Items[installationId].AppVersion);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task Register_DifferentSecretForExistingId_DoesNotIssueToken()
    {
        var repository = new FakeRepository();
        InstallationRegistrationService service = CreateService(repository);
        Guid installationId = Guid.NewGuid();
        await service.RegisterAsync(CreateCommand(installationId));

        InstallationRegistrationResult result = await service.RegisterAsync(
            CreateCommand(installationId) with
            {
                InstallationSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            });

        Assert.Equal(InstallationRegistrationStatus.InvalidCredentials, result.Status);
        Assert.Null(result.AccessToken);
        Assert.Equal(0, repository.SaveCount);
    }

    [Theory]
    [InlineData("desktop", "valid-version")]
    [InlineData("android", "")]
    public async Task Register_InvalidMetadata_IsRejected(string platform, string appVersion)
    {
        var repository = new FakeRepository();
        InstallationRegistrationService service = CreateService(repository);

        InstallationRegistrationResult result = await service.RegisterAsync(
            CreateCommand(Guid.NewGuid()) with
            {
                Platform = platform,
                AppVersion = appVersion,
            });

        Assert.Equal(InstallationRegistrationStatus.InvalidRequest, result.Status);
        Assert.Empty(repository.Items);
    }

    private static InstallationRegistrationService CreateService(FakeRepository repository)
    {
        return new InstallationRegistrationService(
            repository,
            new Sha256Hasher(),
            new FakeTokenIssuer(),
            new FixedTimeProvider(Now));
    }

    private static InstallationRegistrationCommand CreateCommand(Guid installationId)
    {
        return new InstallationRegistrationCommand(
            installationId,
            Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray()),
            "android",
            "1.0.0");
    }

    private sealed class FakeRepository : IInstallationRepository
    {
        public Dictionary<Guid, Installation> Items { get; } = [];

        public int SaveCount { get; private set; }

        public Task<Installation?> FindAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Items.TryGetValue(id, out Installation? installation);
            return Task.FromResult(installation);
        }

        public Task<bool> TryAddAsync(
            Installation installation,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items.TryAdd(installation.Id, installation));
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class Sha256Hasher : IInstallationSecretHasher
    {
        public byte[] Hash(ReadOnlySpan<byte> secret) => SHA256.HashData(secret);
    }

    private sealed class FakeTokenIssuer : IInstallationAccessTokenIssuer
    {
        public InstallationAccessToken Issue(Installation installation, DateTimeOffset issuedAtUtc)
        {
            return new InstallationAccessToken("test-token", issuedAtUtc.AddMinutes(15));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
