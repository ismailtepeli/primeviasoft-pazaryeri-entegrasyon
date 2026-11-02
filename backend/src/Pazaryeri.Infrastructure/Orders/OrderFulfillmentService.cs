using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Credentials;
using Pazaryeri.Application.Jobs;
using Pazaryeri.Application.Marketplace;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Infrastructure.Orders;

public enum FulfillmentAction { Picking, Invoice }

/// <param name="Updated">Kanalın kabul ettiği paket sayısı.</param>
public record FulfillmentResult(string OrderNumber, int Updated, OrderStatus Status, string ChannelStatus);

/// <summary>
/// Pazaryeri siparişinin durumunu kanala geri yazar: Created → Picking (hazırlanıyor), Picking → Invoiced (fatura numarasıyla).
/// Shipped ve Delivered satıcı tarafından değil kargo firması hareketiyle oluşur; onlar webhook ve sipariş senkronuyla gelir.
/// Yalnızca kanalın kabul ettiği paketler yerelde ilerler; hata olursa o ana kadar kabul edilenler korunur.
/// </summary>
public class OrderFulfillmentService(AppDbContext db, IChannelCredentialService credentials, IMarketplaceClientFactory clients, IJobQueue queue)
{
    public const int MaxInvoiceNumberLength = 64;

    public async Task<FulfillmentResult> ActAsync(Guid orderId, FulfillmentAction action, string? invoiceNumber, CancellationToken ct)
    {
        var order = await db.Orders.Include(o => o.Packages).ThenInclude(p => p.Lines).FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new StoreException(404, "Sipariş bulunamadı.");
        if (order.DealerId is not null) throw new StoreException(400, "B2B siparişleri pazaryerine bildirilmez; B2B sipariş ekranını kullanın.");

        invoiceNumber = invoiceNumber?.Trim();
        if (action == FulfillmentAction.Invoice)
        {
            if (string.IsNullOrEmpty(invoiceNumber)) throw new StoreException(400, "Fatura numarası zorunlu.");
            if (invoiceNumber.Length > MaxInvoiceNumberLength) throw new StoreException(400, $"Fatura numarası en fazla {MaxInvoiceNumberLength} karakter olabilir.");
        }

        var (from, to) = action == FulfillmentAction.Picking ? ("Created", "Picking") : ("Picking", "Invoiced");
        var eligible = order.Packages.Where(p => OrderStatusMapper.IsActive(p.Status) && string.Equals(p.ChannelStatus, from, StringComparison.OrdinalIgnoreCase)).ToList();
        if (eligible.Count == 0)
            throw new StoreException(409, action == FulfillmentAction.Picking
                ? "Hazırlanıyor yapılabilecek paket yok (paketler 'Created' durumunda olmalı)."
                : "Faturalanabilecek paket yok (paketler önce hazırlanıyor durumuna alınmalı).");

        var creds = await credentials.GetForClientAsync(order.Channel, ct) ?? throw new StoreException(400, $"{order.Channel} için kayıtlı API bilgisi yok.");
        IMarketplaceClient client;
        try { client = clients.Create(creds); }
        catch (NotSupportedException) { throw new StoreException(400, $"{order.Channel} kanalı durum bildirimini desteklemiyor."); }

        var updated = 0;
        try
        {
            foreach (var package in eligible)
            {
                var lines = package.Lines.Select(l => new OrderStatusLine(l.LineId, l.Quantity)).ToList();
                try { await client.UpdateOrderStatusAsync(new OrderStatusUpdate(package.PackageId, to, lines, action == FulfillmentAction.Invoice ? invoiceNumber : null), ct); }
                catch (MarketplaceApiException e)
                {
                    var body = e.Body.Length > 300 ? e.Body[..300] : e.Body;
                    throw new StoreException(502, $"{order.Channel} paket {package.PackageId} için durumu reddetti ({e.StatusCode}): {body}");
                }
                // LastModifiedAt'e dokunulmaz: kanalın sonraki (daha yeni) bildirimi bu yerel ilerlemenin üzerine yazabilmeli.
                package.ChannelStatus = to;
                package.Status = OrderStatusMapper.Map(order.Channel, to);
                updated++;
            }
        }
        finally
        {
            if (updated > 0)
            {
                OrderIngestService.Recalculate(order);
                await db.SaveChangesAsync(CancellationToken.None);
                try { await queue.EnqueueAsync(SyncJobType.OrderSync, order.Channel, ct: CancellationToken.None); }
                catch (Exception) { /* uzlaştırma senkronu zamanlayıcıyla da çalışır; kuyruğa alınamaması işlemi bozmaz */ }
            }
        }
        return new FulfillmentResult(order.OrderNumber, updated, order.Status, to);
    }
}
