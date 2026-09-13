namespace Nuventra.NuvexaDB;

/// <summary>Base exception for NuvexaDB failures.</summary>
public class NuvexaException : Exception
{
    public NuvexaException(string message) : base(message)
    {
    }

    public NuvexaException(string message, Exception inner) : base(message, inner)
    {
    }
}

/// <summary>
/// Thrown when a <c>.nvx</c> file is encrypted and the key is missing or wrong.
/// The library is fail-closed: it does not create, overwrite, or partially open the file.
/// </summary>
public sealed class NuvexaEncryptionException : NuvexaException
{
    public NuvexaEncryptionException(string message) : base(message)
    {
    }

    public NuvexaEncryptionException(string message, Exception inner) : base(message, inner)
    {
    }
}

/// <summary>
/// Thrown when a <c>.nvx</c> file fails an integrity check (superblock CRC, page CRC,
/// AES-GCM tag, or encrypted superblock HMAC). The file is treated as invalid and is
/// not opened. The library does not repair or overwrite the file.
/// </summary>
public sealed class NuvexaIntegrityException : NuvexaException
{
    public NuvexaIntegrityException(string message) : base(message)
    {
    }

    public NuvexaIntegrityException(string message, Exception inner) : base(message, inner)
    {
    }
}
