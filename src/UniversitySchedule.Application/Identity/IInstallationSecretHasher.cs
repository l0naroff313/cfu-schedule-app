namespace UniversitySchedule.Application.Identity;

public interface IInstallationSecretHasher
{
    byte[] Hash(ReadOnlySpan<byte> secret);
}
