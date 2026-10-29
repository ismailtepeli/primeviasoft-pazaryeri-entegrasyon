using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Marketplace;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Marketplace.Trendyol;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Infrastructure.Marketplace;

public class MarketplaceClientFactory(IHttpClientFactory httpFactory) : IMarketplaceClientFactory
{
    public const string HttpClientName = "marketplace";

    public IMarketplaceClient Create(MarketplaceCredentials credentials) => credentials.Channel switch
    {
        TrendyolClient.ChannelName => new TrendyolClient(httpFactory.CreateClient(HttpClientName), credentials),
        _ => throw new NotSupportedException($"'{credentials.Channel}' kanalı için adapter yok."),
    };
}

/// <summary>
/// API çağrı kayıtlarını veritabanına yazar. HttpClient handler'ları kendi DI kapsamında yaşadığı için
/// tekil (singleton) çalışır ve her yazmada yeni kapsam açar. Kiracı bilinmiyorsa yalnızca ILogger'a yazılmış olur.
/// </summary>
public class ApiCallLogger(IServiceScopeFactory scopes, ILogger<ApiCallLogger> logger) : IApiCallLogger
{
    public async Task LogAsync(ApiCallEntry e)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var tenantId = scope.ServiceProvider.GetRequiredService<ITenantProvider>().TenantId;
            if (tenantId is null) return;

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ApiCallLogs.Add(new ApiCallLog
            {
                TenantId = tenantId.Value,
                Channel = e.Channel, SellerId = e.SellerId, Method = e.Method, Url = e.Url,
                RequestBody = e.RequestBody, StatusCode = e.StatusCode, ResponseBody = e.ResponseBody,
                DurationMs = e.DurationMs, Error = e.Error,
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Log yazılamaması asıl API çağrısını bozmamalı.
            logger.LogWarning(ex, "API çağrı kaydı yazılamadı.");
        }
    }
}
