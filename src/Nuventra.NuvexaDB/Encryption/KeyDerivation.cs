using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Nuventra.NuvexaDB.Engine;

namespace Nuventra.NuvexaDB.Encryption;

internal static class KeyDerivation
{
    public static byte[] DeriveKek(string password, ReadOnlySpan<byte> salt, int memoryKb, int iterations, int parallelism)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new NuvexaEncryptionException("An encryption key is required.");
        }

        if (memoryKb < 8 * 1024 || memoryKb > 256 * 1024)
        {
            throw new NuvexaException("Argon2id memory must be between 8 MiB and 256 MiB.");
        }

        if (iterations < 1 || iterations > 16)
        {
            throw new NuvexaException("Argon2id iterations must be between 1 and 16.");
        }

        if (parallelism < 1 || parallelism > 8)
        {
            throw new NuvexaException("Argon2id parallelism must be between 1 and 8.");
        }

        var passwordBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            using var argon = new Argon2id(passwordBytes);
            argon.Salt = salt.ToArray();
            argon.MemorySize = memoryKb;
            argon.Iterations = iterations;
            argon.DegreeOfParallelism = parallelism;
            return argon.GetBytes(32);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    public static byte[] GenerateDek() => RandomNumberGenerator.GetBytes(32);

    public static byte[] GenerateSalt() => RandomNumberGenerator.GetBytes(16);

    public static void VerifyKek(byte[] kek, Superblock superblock)
    {
        try
        {
            var plain = AesGcmPageCipher.DecryptRaw(
                kek,
                superblock.VerifierNonce,
                superblock.VerifierCipher,
                superblock.VerifierTag,
                superblock.FileId);
            if (!plain.AsSpan().SequenceEqual(Constants.VerifierPlaintext))
            {
                throw new NuvexaEncryptionException("The encryption key is incorrect.");
            }
        }
        catch (NuvexaEncryptionException)
        {
            throw;
        }
        catch (CryptographicException ex)
        {
            throw new NuvexaEncryptionException("The encryption key is incorrect.", ex);
        }
    }

    public static byte[] UnwrapDek(byte[] kek, Superblock superblock)
    {
        try
        {
            return AesGcmPageCipher.DecryptRaw(
                kek,
                superblock.DekNonce,
                superblock.DekCipher,
                superblock.DekTag,
                superblock.FileId);
        }
        catch (CryptographicException ex)
        {
            throw new NuvexaEncryptionException("The encryption key is incorrect.", ex);
        }
    }

    public static void WrapKeys(Superblock superblock, byte[] kek, byte[] dek)
    {
        AesGcmPageCipher.EncryptRaw(kek, Constants.VerifierPlaintext, superblock.FileId,
            out var verifierNonce, out var verifierCipher, out var verifierTag);
        AesGcmPageCipher.EncryptRaw(kek, dek, superblock.FileId,
            out var dekNonce, out var dekCipher, out var dekTag);
        superblock.VerifierNonce = verifierNonce;
        superblock.VerifierCipher = verifierCipher;
        superblock.VerifierTag = verifierTag;
        superblock.DekNonce = dekNonce;
        superblock.DekCipher = dekCipher;
        superblock.DekTag = dekTag;
    }
}
