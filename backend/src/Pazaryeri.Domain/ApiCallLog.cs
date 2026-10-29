using Pazaryeri.Domain.Common;

namespace Pazaryeri.Domain;

/// <summary>Pazaryeri API çağrısının istek/yanıt kaydı; mağaza (SellerId) bazında ayrıştırılır.</summary>
public class ApiCallLog : TenantEntity
{
    public string Channel { get; set; } = string.Empty;
    public string SellerId { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? RequestBody { get; set; }
    public int? StatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public long DurationMs { get; set; }
    public string? Error { get; set; }
}
