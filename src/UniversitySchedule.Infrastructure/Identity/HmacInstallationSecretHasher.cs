using System.Security.Cryptography;
using System.Text;
using UniversitySchedule.Application.Identity;

namespace UniversitySchedule.Infrastructure.Identity;

public sealed class HmacInstallationSecretHasher : IInstallationSecretHasher
{
    private readonly byte[] _pepper;

    public HmacInstallationSecretHasher(string pepper)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pepper);
        if (Encoding.UTF8.GetByteCount(pepper) < 32)
        {
            throw new ArgumentException("The installation secret pepper must contain at least 32 UTF-8 bytes.", nameof(pepper));
        }

        _pepper = Encoding.UTF8.GetBytes(pepper);
    }

    public byte[] Hash(ReadOnlySpan<byte> secret)
    {
        if (secret.IsEmpty)
        {
            throw new ArgumentException("The installation secret cannot be empty.", nameof(secret));
        }

        return HMACSHA256.HashData(_pepper, secret);
    }
}
