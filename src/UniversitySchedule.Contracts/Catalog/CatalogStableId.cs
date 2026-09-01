using System.Security.Cryptography;
using System.Text;

namespace UniversitySchedule.Contracts.Catalog;

public static class CatalogStableId
{
    public static Guid Create(params string?[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        string key = string.Join(
            '\u001f',
            parts.Select(part => Normalize(part ?? string.Empty)));
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        Span<byte> identifier = stackalloc byte[16];
        hash.AsSpan(0, identifier.Length).CopyTo(identifier);

        identifier[7] = (byte)((identifier[7] & 0x0f) | 0x50);
        identifier[8] = (byte)((identifier[8] & 0x3f) | 0x80);
        return new Guid(identifier);
    }

    private static string Normalize(string value)
    {
        return string.Join(
                ' ',
                value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant()
            .Replace('ё', 'е');
    }
}
