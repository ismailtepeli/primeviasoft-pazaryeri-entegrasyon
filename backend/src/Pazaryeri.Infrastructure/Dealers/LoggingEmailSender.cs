using Microsoft.Extensions.Logging;
using Pazaryeri.Application.Abstractions;

namespace Pazaryeri.Infrastructure.Dealers;

/// <summary>Gerçek e-posta sağlayıcısı bağlanana kadar iletiyi günlüğe yazar (geliştirme).</summary>
public class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        logger.LogInformation("E-posta (gönderilmedi) -> {To} | {Subject}\n{Body}", to, subject, body);
        return Task.CompletedTask;
    }
}
