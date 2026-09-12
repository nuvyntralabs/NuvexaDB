using System.Buffers.Binary;
using System.Security.Cryptography;
using Nuventra.NuvexaDB.Engine;

namespace Nuventra.NuvexaDB.Encryption;

/// <summary>
/// AES-256-GCM for page payloads. One instance per open store so we do not
/// construct <see cref="AesGcm"/> on every page read or write.
/// </summary>
internal sealed class AesGcmPageCipher : IDisposable
{
    private readonly AesGcm _gcm;
    private readonly object _sync = new();
    private bool _disposed;

    public AesGcmPageCipher(byte[] dek)
    {
        _gcm = new AesGcm(dek, Constants.TagSize);
    }

    public void EncryptPage(long pageId, ReadOnlySpan<byte> fileId, ReadOnlySpan<byte> logical, Span<byte> physical)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (logical.Length != Constants.PayloadSize || physical.Length != Constants.PageSize)
        {
            throw new NuvexaException("Encrypted page buffers have the wrong size.");
        }

        var nonce = physical[..Constants.NonceSize];
        RandomNumberGenerator.Fill(nonce);
        var tag = physical.Slice(Constants.NonceSize, Constants.TagSize);
        var cipher = physical[Constants.CipherOverhead..];
        Span<byte> aad = stackalloc byte[24];
        WriteAad(aad, fileId, pageId);
        lock (_sync)
        {
            _gcm.Encrypt(nonce, logical, cipher, tag, aad);
        }
    }

    public void DecryptPage(long pageId, ReadOnlySpan<byte> fileId, ReadOnlySpan<byte> physical, Span<byte> logical)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (logical.Length != Constants.PayloadSize || physical.Length != Constants.PageSize)
        {
            throw new NuvexaException("Encrypted page buffers have the wrong size.");
        }

        var nonce = physical[..Constants.NonceSize];
        var tag = physical.Slice(Constants.NonceSize, Constants.TagSize);
        var cipher = physical[Constants.CipherOverhead..];
        Span<byte> aad = stackalloc byte[24];
        WriteAad(aad, fileId, pageId);
        try
        {
            lock (_sync)
            {
                _gcm.Decrypt(nonce, cipher, tag, logical, aad);
            }
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gcm.Dispose();
    }

    private static void WriteAad(Span<byte> aad, ReadOnlySpan<byte> fileId, long pageId)
    {
        fileId[..16].CopyTo(aad);
        BinaryPrimitives.WriteInt64LittleEndian(aad[16..], pageId);
    }
}
