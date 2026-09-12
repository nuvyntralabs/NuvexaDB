using System.Security.Cryptography;

namespace Nuventra.NuvexaDB;

/// <summary>Mongo-style 12-byte object id rendered as 24 hex characters.</summary>
public static class NuvexaObjectId
{
    public static string NewId()
    {
        var bytes = new byte[12];
        var seconds = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        bytes[0] = (byte)(seconds >> 24);
        bytes[1] = (byte)(seconds >> 16);
        bytes[2] = (byte)(seconds >> 8);
        bytes[3] = (byte)seconds;
        RandomNumberGenerator.Fill(bytes.AsSpan(4));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
