using System.Security.Cryptography;
using System.Text;

namespace Workflow.Api.Data.Seeding;

// Name-based (UUID v5 style) IDs: the same key always yields the same GUID, so seeding is idempotent.
public static class SeedIds
{
    private static readonly byte[] Namespace = Guid.Parse("3f1b8c52-6a0e-4c1e-9d55-1f0b6c2e7a90").ToByteArray(bigEndian: true);

    public static Guid Of(string key)
    {
        var name = Encoding.UTF8.GetBytes(key);
        var hash = SHA1.HashData([.. Namespace, .. name]);
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }
}
