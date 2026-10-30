using Pazaryeri.Application.Marketplace;
using Pazaryeri.Application.Products;
using Pazaryeri.Domain;

namespace Pazaryeri.Infrastructure.Products;

public enum BatchOutcome
{
    /// <summary>Kanal henüz bitirmedi; sonraki turda yeniden sorgulanır.</summary>
    StillPending,
    Completed,
    /// <summary>Süre doldu, sonuç gelmedi.</summary>
    TimedOut,
}

/// <summary>Kanalın toplu işlem sonucunu ürünlerin kanal durumuna ve gönderim işine yazar. Saf mantık; veritabanına dokunmaz.</summary>
public static class ProductBatchApplier
{
    public const string TimeoutMessage = "Kanal 4 saat içinde sonuç bildirmedi.";
    public const string MissingFromResultMessage = "Kanal sonuçta bu ürünü bildirmedi.";

    /// <summary>Tüm kalemler sonuçlandıysa ya da kanal toplu işi tamamlandı dediyse bitmiştir.</summary>
    public static bool IsDone(BatchStatus status) =>
        status.Status.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase)
        || status.Status.Equals("FAILED", StringComparison.OrdinalIgnoreCase)
        || (status.Items.Count > 0 && status.Items.All(i => IsFinal(i.Status)));

    private static bool IsFinal(string s) =>
        s.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase) || s.Equals("FAILED", StringComparison.OrdinalIgnoreCase);

    /// <param name="status">Kanaldan alınan sonuç; sorgulanamadıysa null (yalnızca süre aşımı denetlenir).</param>
    /// <param name="barcodes">Üründen barkoda eşleme (gönderilen ürünler).</param>
    public static BatchOutcome Apply(SyncJob job, IReadOnlyDictionary<Guid, string> barcodes, IReadOnlyList<ProductListing> listings,
        BatchStatus? status, DateTime nowUtc)
    {
        var sentAt = job.BatchSentAt ?? job.CreatedAt;

        if (status is not null && IsDone(status))
        {
            var byBarcode = status.Items.GroupBy(i => i.Barcode, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
            var failed = 0;
            foreach (var listing in listings.Where(l => l.Status == ListingStatus.Pending))
            {
                if (!barcodes.TryGetValue(listing.ProductId, out var barcode)) continue;
                if (!byBarcode.TryGetValue(barcode, out var item)) { Fail(listing, MissingFromResultMessage); failed++; continue; }
                if (item.Status.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase))
                {
                    listing.ErrorMessage = null; // kanal kabul etti; onay süreci sürüyor (Pending kalır)
                }
                else
                {
                    var reasons = item.FailureReasons.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct().ToList();
                    Fail(listing, reasons.Count > 0 ? string.Join('\n', reasons) : "Kanal ürünü reddetti (neden bildirilmedi).");
                    failed++;
                }
            }
            Close(job, failed, nowUtc);
            return BatchOutcome.Completed;
        }

        if (nowUtc - sentAt > IProductSendService.BatchTimeout)
        {
            var timedOut = 0;
            foreach (var listing in listings.Where(l => l.Status == ListingStatus.Pending)) { Fail(listing, TimeoutMessage); timedOut++; }
            Close(job, timedOut, nowUtc, TimeoutMessage);
            return BatchOutcome.TimedOut;
        }
        return BatchOutcome.StillPending;
    }

    private static void Fail(ProductListing listing, string message)
    {
        listing.Status = ListingStatus.Error;
        listing.ErrorMessage = message.Length > 2000 ? message[..2000] : message;
    }

    private static void Close(SyncJob job, int failed, DateTime nowUtc, string? error = null)
    {
        job.BatchPending = false;
        job.FinishedAt = nowUtc;
        if (failed == 0) return;
        job.Status = SyncJobStatus.Failed;
        job.AffectedCount += failed;
        job.Error = error ?? $"{failed} ürün kanal tarafından reddedildi.";
    }
}
