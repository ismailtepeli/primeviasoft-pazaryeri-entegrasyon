using System.Text.Json;
using Microsoft.Extensions.Options;
using Pazaryeri.Application.Amazon;

namespace Pazaryeri.Infrastructure.Amazon;

/// <summary>LWA token uç noktası: Amazon'un verdiği tek kullanımlık kodu uzun ömürlü refresh token ile takas eder.</summary>
public class AmazonLwaClient(HttpClient http, IOptions<AmazonLwaOptions> options)
{
    public const string HttpClientName = "amazon-lwa";

    public async Task<string> ExchangeCodeAsync(string code, CancellationToken ct = default)
    {
        var o = options.Value;
        using var response = await http.PostAsync(o.TokenUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = o.ClientId,
            ["client_secret"] = o.ClientSecret,
            ["redirect_uri"] = o.RedirectUri,
        }), ct);

        var text = await response.Content.ReadAsStringAsync(ct);
        string? error = null, refresh = null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("error", out var e)) error = e.GetString();
            if (doc.RootElement.TryGetProperty("refresh_token", out var r)) refresh = r.GetString();
        }
        catch (JsonException) { /* yanıt JSON değil; aşağıda genel hata verilir */ }

        if (response.IsSuccessStatusCode && !string.IsNullOrEmpty(refresh)) return refresh;

        // Ham hata gövdesi (kod, secret içerebilir) kullanıcıya ve loglara yansıtılmaz.
        throw new AmazonOAuthException(error switch
        {
            "invalid_grant" => "Yetkilendirme kodu geçersiz veya süresi dolmuş; işlemi baştan başlatın.",
            "invalid_client" or "unauthorized_client" => "Amazon uygulama kimliği (client id/secret) reddedildi; client secret'ın süresi dolmuş olabilir.",
            _ => "Amazon yetkilendirmeyi tamamlayamadı.",
        });
    }
}
