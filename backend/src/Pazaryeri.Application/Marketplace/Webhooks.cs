using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Pazaryeri.Application.Marketplace;

public enum WebhookOutcome { Accepted, Duplicate, Ignored, Unauthorized }

/// <summary>Webhook bildirimindeki paket kimliği ve durum; gövde beklenen biçimde değilse boş kalır (senkron yine tetiklenir).</summary>
public record WebhookPayload(string? PackageId, string? Status)
{
    private static readonly string[] PackageKeys = ["shipmentPackageId", "packageId", "id"];
    private static readonly string[] StatusKeys = ["shipmentPackageStatus", "status", "packageStatus"];

    /// <summary>Gövdeyi hoşgörüyle okur: üst düzey ya da "data"/"content" içindeki ilk nesnedeki bilinen alan adlarını arar.</summary>
    public static WebhookPayload Read(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return new(null, null);
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            foreach (var wrapper in new[] { "data", "content", "payload" })
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(wrapper, out var inner) && inner.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                { root = inner; break; }
            if (root.ValueKind == JsonValueKind.Array) root = root.GetArrayLength() > 0 ? root[0] : default;
            if (root.ValueKind != JsonValueKind.Object) return new(null, null);
            return new(Pick(root, PackageKeys), Pick(root, StatusKeys));
        }
        catch (JsonException) { return new(null, null); }
    }

    private static string? Pick(JsonElement obj, string[] keys)
    {
        foreach (var k in keys)
            if (obj.TryGetProperty(k, out var v) && v.ValueKind is JsonValueKind.String or JsonValueKind.Number)
            {
                var text = v.ToString().Trim();
                if (text.Length > 0 && text.Length <= 100) return text;
            }
        return null;
    }

    /// <summary>İdempotentlik anahtarı: paket ve durum birlikte biliniyorsa "paket:durum", yoksa null (dedupe yapılmaz).</summary>
    public string? EventKey => PackageId is not null && Status is not null ? $"{PackageId}:{Status}" : null;
}

/// <summary>Webhook gizli anahtarı üretimi ve doğrulaması. Anahtar yalnızca özet olarak saklanır; karşılaştırma sabit zamanlıdır.</summary>
public static class WebhookSecret
{
    public const string Username = "pazaryeri";

    public static string Generate() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();

    public static bool Verify(string? provided, string storedHash)
    {
        if (string.IsNullOrEmpty(provided) || string.IsNullOrEmpty(storedHash)) return false;
        var a = Encoding.ASCII.GetBytes(Hash(provided));
        var b = Encoding.ASCII.GetBytes(storedHash);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>Kanalın gönderdiği kimlik doğrulamasından gizli anahtarı çıkarır: Basic parolası, Bearer belirteci ya da x-api-key.</summary>
    public static string? Extract(string? authorization, string? apiKey)
    {
        if (!string.IsNullOrWhiteSpace(apiKey)) return apiKey.Trim();
        if (string.IsNullOrWhiteSpace(authorization)) return null;
        var parts = authorization.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return null;
        if (parts[0].Equals("Bearer", StringComparison.OrdinalIgnoreCase)) return parts[1].Trim();
        if (!parts[0].Equals("Basic", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(parts[1].Trim()));
            var i = decoded.IndexOf(':');
            return i < 0 ? null : decoded[(i + 1)..];
        }
        catch (FormatException) { return null; }
    }
}

/// <summary>Kanala webhook kaydı için gönderilen bilgiler.</summary>
public record WebhookRegistration(string Url, string Username, string Password, IReadOnlyList<string> Statuses);

public record WebhookRegistrationResult(string? WebhookId);

/// <summary>Kanalın webhook sunucu adresi; boşsa kayıt yapılamaz (yerelde herkese açık adres yoktur).</summary>
public record WebhookOptions(string? PublicBaseUrl);
