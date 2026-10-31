namespace Pazaryeri.Application.Abstractions;

/// <summary>E-posta gönderimi. Gerçek sağlayıcı (SMTP, SendGrid vb.) seçilene kadar geliştirmede günlüğe yazan uygulama kullanılır.</summary>
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct);
}

/// <summary>Bayi sitesi bağlantı şablonları. {slug} ve {token} yer tutucularını içerir.</summary>
public record StoreLinkOptions(string LoginUrl, string ResetUrl)
{
    public static readonly StoreLinkOptions Default = new("http://localhost:5174/t/{slug}/giris", "http://localhost:5174/t/{slug}/sifre-sifirla?token={token}");
    public string Login(string? slug) => LoginUrl.Replace("{slug}", slug ?? "");
    public string Reset(string? slug, string token) => ResetUrl.Replace("{slug}", slug ?? "").Replace("{token}", Uri.EscapeDataString(token));
}
