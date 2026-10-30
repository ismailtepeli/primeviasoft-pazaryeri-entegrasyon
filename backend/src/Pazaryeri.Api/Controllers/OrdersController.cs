using Pazaryeri.Api.Modules;
using Pazaryeri.Application.Modules;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Orders;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Api.Controllers;

/// <param name="Status">"New", "Preparing", "Shipped", "Delivered", "Cancelled" veya "Returned".</param>
/// <param name="ChannelStatus">Kanalın bildirdiği ham durum (en yeni paketin).</param>
/// <param name="IsLate">Süresi içinde işlem görmedi (yeni: 24 sa, hazırlanıyor: 48 sa).</param>
public record OrderRowDto(Guid Id, string Channel, string OrderNumber, DateTime OrderDate, string CustomerName, decimal TotalPrice,
    string Status, string ChannelStatus, int ItemCount, bool IsLate);

/// <summary>Sekme sayaçları; arama ve sayfadan bağımsız, tüm siparişler üzerinden.</summary>
public record OrderCounts(int New, int Preparing, int Shipped, int Delivered, int Cancelled);

public record OrderListPage(IReadOnlyList<OrderRowDto> Items, int Total, OrderCounts Counts);

[ApiController]
[Route("api/orders")]
[RequiresModule(ModuleIds.Orders)]
public class OrdersController(AppDbContext db, TimeProvider time) : ControllerBase
{
    public const int MaxPageSize = 100;

    /// <summary>Birleşik sipariş listesi: sekme (durum), arama (sipariş no / müşteri) ve sunucu taraflı sayfalama; en yeni önce.</summary>
    [HttpGet]
    public async Task<ActionResult<OrderListPage>> List([FromQuery] string? tab, [FromQuery] string? q,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? channel = null, CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        page = Math.Max(page, 1);
        var statuses = OrderRules.StatusesForTab(tab);
        if (tab is not null && statuses is null) return BadRequest(new { error = "Bilinmeyen sekme." });

        var all = db.Orders.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(channel)) all = all.Where(o => o.Channel == channel);
        var byStatus = await all.GroupBy(o => o.Status).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        int Count(params OrderStatus[] s) => s.Sum(x => byStatus.GetValueOrDefault(x));
        var counts = new OrderCounts(Count(OrderStatus.New), Count(OrderStatus.Preparing), Count(OrderStatus.Shipped), Count(OrderStatus.Delivered), Count(OrderStatus.Cancelled, OrderStatus.Returned));

        var query = all;
        if (statuses is not null) query = query.Where(o => statuses.Contains(o.Status));
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(o => o.OrderNumber.Contains(term) || o.CustomerName.Contains(term));
        }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(o => o.OrderDate).ThenByDescending(o => o.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(o => new
            {
                o.Id, o.Channel, o.OrderNumber, o.OrderDate, o.CustomerName, o.TotalPrice, o.Status,
                ChannelStatus = o.Packages.OrderByDescending(p => p.LastModifiedAt).Select(p => p.ChannelStatus).FirstOrDefault(),
                ItemCount = o.Packages.Where(p => p.Status != OrderStatus.Cancelled && p.Status != OrderStatus.Returned).SelectMany(p => p.Lines).Sum(l => (int?)l.Quantity) ?? 0,
            }).ToListAsync(ct);

        var now = time.GetUtcNow().UtcDateTime;
        var items = rows.Select(o => new OrderRowDto(o.Id, o.Channel, o.OrderNumber, o.OrderDate, o.CustomerName, o.TotalPrice, o.Status.ToString(),
            o.ChannelStatus ?? "", o.ItemCount, OrderRules.IsLate(o.Status, o.OrderDate, now))).ToList();
        return new OrderListPage(items, total, counts);
    }
}
