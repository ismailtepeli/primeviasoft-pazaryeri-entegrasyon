using System.Security.Cryptography;
using System.Text;
using Pazaryeri.Application.Credentials;

namespace Pazaryeri.Infrastructure.Security;

/// <summary>
/// AES-256-GCM. Çıktı: "v1:" + base64(nonce[12] | etiket[16] | şifreli veri). Her şifrelemede yeni nonce kullanılır.
/// Bağlam (kiracı:kanal:ortam) ilişkili veri olarak doğrulanır; şifreli metin başka bir satıra kopyalanırsa çözülemez.
/// "v1:" öneki anahtar döndürme (rotation) için yer bırakır.
/// </summary>
public sealed class AesGcmCredentialProtector : ICredentialProtector
{
    private const string Prefix = "v1:";
    private const int NonceSize = 12, TagSize = 16;
    private readonly byte[] _key;

    public AesGcmCredentialProtector(byte[] key)
    {
        if (key.Length != 32) throw new ArgumentException("Şifreleme anahtarı tam 32 bayt (AES-256) olmalı.", nameof(key));
        _key = key;
    }

    public static AesGcmCredentialProtector FromBase64(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
            throw new InvalidOperationException("Credentials:Key tanımlı değil (base64, 32 bayt). Ortam değişkeni veya Key Vault ile verin.");
        try { return new AesGcmCredentialProtector(Convert.FromBase64String(base64)); }
        catch (FormatException) { throw new InvalidOperationException("Credentials:Key geçerli bir base64 değil."); }
    }

    public string Protect(string plaintext, string context)
    {
        var data = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[data.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, data, cipher, tag, Encoding.UTF8.GetBytes(context));
        return Prefix + Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    public string Unprotect(string protectedText, string context)
    {
        if (!protectedText.StartsWith(Prefix, StringComparison.Ordinal))
            throw new CryptographicException("Bilinmeyen şifreli veri biçimi.");
        var raw = Convert.FromBase64String(protectedText[Prefix.Length..]);
        if (raw.Length < NonceSize + TagSize) throw new CryptographicException("Şifreli veri bozuk.");

        var nonce = raw[..NonceSize];
        var tag = raw[NonceSize..(NonceSize + TagSize)];
        var cipher = raw[(NonceSize + TagSize)..];
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain, Encoding.UTF8.GetBytes(context));
        return Encoding.UTF8.GetString(plain);
    }
}
