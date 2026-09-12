using System.Buffers.Binary;
using System.Security.Cryptography;
using Nuventra.NuvexaDB.Engine;

namespace Nuventra.NuvexaDB.Encryption;

internal static class AesGcmPageCipher
{
    public static void EncryptPage(byte[] dek, long pageId, ReadOnlySpan<byte> fileId, ReadOnlySpan<byte> logical, Span<byte> physical)
    {
        if (logical.Length != Constants.PayloadSize || physical.Length != Constants.PageSize)
        {
            throw new NuvexaException("Encrypted page buffers have the wrong size.");
        }

        var nonce = physical[..Constants.NonceSize];
        RandomNumberGenerator.Fill(nonce);
        var tag = physical.Slice(Constants.NonceSize, Constants.TagSize);
        var cipher = physical[Constants.CipherOverhead..];
        var aad = BuildAad(fileId, pageId);
        using var gcm = new AesGcm(dek, Constants.TagSize);
        gcm.Encrypt(nonce, logical, cipher, tag, aad);
    }

    public static void DecryptPage(byte[] dek, long pageId, ReadOnlySpan<byte> fileId, ReadOnlySpan<byte> physical, Span<byte> logical)
    {
        if (logical.Length != Constants.PayloadSize || physical.Length != Constants.PageSize)
        {
            throw new NuvexaException("Encrypted page buffers have the wrong size.");
        }

        var nonce = physical[..Constants.NonceSize];
        var tag = physical.Slice(Constants.NonceSize, Constants.TagSize);
        var cipher = physical[Constants.CipherOverhead..];
        var aad = BuildAad(fileId, pageId);
        try
        {
            using var gcm = new AesGcm(dek, Constants.TagSize);
            gcm.Decrypt(nonce, cipher, tag, logical, aad);
        }
        catch (CryptographicException ex)
        {
            throw new NuvexaEncryptionException("The page could not be authenticated. The key may be wrong or the file is corrupt.", ex);
        }
    }

    public static void EncryptRaw(byte[] key, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> aad,
        out byte[] nonce, out byte[] cipher, out byte[] tag)
    {
        nonce = RandomNumberGenerator.GetBytes(Constants.NonceSize);
        cipher = new byte[plaintext.Length];
        tag = new byte[Constants.TagSize];
        using var gcm = new AesGcm(key, Constants.TagSize);
        gcm.Encrypt(nonce, plaintext, cipher, tag, aad);
    }

    public static byte[] DecryptRaw(byte[] key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> cipher, ReadOnlySpan<byte> tag, ReadOnlySpan<byte> aad)
    {
        var plain = new byte[cipher.Length];
        using var gcm = new AesGcm(key, Constants.TagSize);
        gcm.Decrypt(nonce, cipher, tag, plain, aad);
        return plain;
    }

    private static byte[] BuildAad(ReadOnlySpan<byte> fileId, long pageId)
    {
        var aad = new byte[24];
        fileId[..16].CopyTo(aad);
        BinaryPrimitives.WriteInt64LittleEndian(aad.AsSpan(16), pageId);
        return aad;
    }
}
