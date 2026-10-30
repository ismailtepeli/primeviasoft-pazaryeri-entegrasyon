using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Api.Controllers;

/// <param name="Status">"Pending", "Live" veya "Error".</param>
public record ProductChannelDto(string Channel, string Status, string? ErrorMessage);

public record ProductDto(Guid Id, string Name, string Barcode, string? MainId, string? Variant, int Stock, decimal Price, IReadOnlyList<ProductChannelDto> Channels);

public record ProductPage(IReadOnlyList<ProductDto> Items, int Total);

[ApiController]
[Route("api/products")]
public class ProductsController(AppDbContext db) : ControllerBase
{
    public const int MaxPageSize = 100;

    /// <summary>
    /// Katalog listesi. <paramref name="q"/> ad ya da barkodda arar; <paramref name="filter"/>:
    /// "all" (varsayılan), "error" (en az bir kanalda hatalı) veya "pending" (en az bir kanalda onay bekleyen).
    /// </summary>
    [HttpGet]
    public async Task<ProductPage> List([FromQuery] string? q, [FromQuery] string? filter,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        page = Math.Max(page, 1);

        var query = db.Products.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(p => p.Name.Contains(term) || p.Barcode.Contains(term));
        }

        query = filter?.ToLowerInvariant() switch
        {
            "error" => query.Where(p => p.Listings.Any(l => l.Status == ListingStatus.Error)),
            "pending" => query.Where(p => p.Listings.Any(l => l.Status == ListingStatus.Pending)),
            _ => query,
        };

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new { p.Id, p.Name, p.Barcode, p.MainId, p.Variant, p.Stock, p.Price, Listings = p.Listings.Select(l => new { l.Channel, l.Status, l.ErrorMessage }).ToList() })
            .ToListAsync(ct);

        var items = rows.Select(r => new ProductDto(r.Id, r.Name, r.Barcode, r.MainId, r.Variant, r.Stock, r.Price,
            r.Listings.OrderBy(l => l.Channel).Select(l => new ProductChannelDto(l.Channel, l.Status.ToString(), l.ErrorMessage)).ToList())).ToList();
        return new ProductPage(items, total);
    }
}
