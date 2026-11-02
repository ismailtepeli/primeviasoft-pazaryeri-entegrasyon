using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Modules;
using Pazaryeri.Domain;

namespace Pazaryeri.Infrastructure.Persistence;

public record DemoSeedResult(int Categories, int Products, int Dealers, int Customers = 0);

/// <summary>
/// Geliştirme ortamı için tekrarlanabilir demo veri (<c>dotnet run --project src/Pazaryeri.Api -- seed-demo</c>): birkaç kategori,
/// "DEMO-" barkodlu ürünler (B2B ve B2C'de görünür, biri kampanya fiyatlı), bir bayi grubu ve giriş yapabilen bir bayi; B2B bayi sitesi ve (geliştirme için) B2C mağaza modülleri açılır. Ne eksikse onu ekler,
/// var olana dokunmaz; kaç kez çalıştırılsa aynı sonucu verir. Yalnızca Development ortamında çalıştırılır.
/// </summary>
public class DemoDataSeeder(AppDbContext db, IConfiguration config)
{
    public const string BarcodePrefix = "DEMO-";

    private static readonly (string Name, string Category, string Brand, decimal Price, int Stock, int Min, int Pack)[] Products =
    [
        ("Basic Tişört", "Giyim", "Demo Giyim", 149.90m, 120, 1, 1),
        ("Polo Yaka Tişört", "Giyim", "Demo Giyim", 199.90m, 80, 1, 1),
        ("A4 Çizgili Defter 96 Yaprak", "Kırtasiye", "Demo Kırtasiye", 31.41m, 500, 24, 12),
        ("Tükenmez Kalem Mavi (12'li)", "Kırtasiye", "Demo Kırtasiye", 42.00m, 300, 1, 1),
        ("Yapışkan Not Kağıdı 76x76", "Kırtasiye", "Demo Kırtasiye", 49.41m, 240, 12, 12),
        ("Bambu Kesme Tahtası Büyük", "Ev ve Yaşam", "Demo Ev", 289.00m, 40, 1, 1),
        ("Cam Saklama Kabı 1 L", "Ev ve Yaşam", "Demo Ev", 94.50m, 150, 6, 6),
        ("USB-C Hızlı Şarj Kablosu 1 m", "Elektronik", "Demo Elektronik", 129.00m, 200, 1, 1),
        ("Kablosuz Mouse", "Elektronik", "Demo Elektronik", 349.00m, 60, 1, 1),
        ("Telefon Kılıfı Silikon", "Elektronik", "Demo Elektronik", 79.90m, 90, 1, 1),
    ];

    public async Task<DemoSeedResult> SeedAsync(CancellationToken ct = default)
    {
        var tenantName = config["Seed:TenantName"];
        if (string.IsNullOrWhiteSpace(tenantName)) throw new InvalidOperationException("Seed:TenantName tanımlı değil; önce geliştirme kiracısını oluşturun.");
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Name == tenantName, ct)
            ?? throw new InvalidOperationException($"'{tenantName}' kiracısı yok; API'yi Development ortamında bir kez açın.");

        using var _ = AmbientTenant.Use(tenant.Id);
        var categories = await EnsureCategoriesAsync(tenant.Id, ct);
        var products = await EnsureProductsAsync(tenant.Id, categories, ct);
        var dealers = await EnsureDealerAsync(tenant.Id, ct);
        await EnsureModulesAsync(tenant.Id, ct);
        var customers = await EnsureCustomerAsync(tenant.Id, ct);
        return new DemoSeedResult(categories.Count, products, dealers, customers);
    }

    private async Task<Dictionary<string, Category>> EnsureCategoriesAsync(Guid tenantId, CancellationToken ct)
    {
        var existing = await db.Categories.Where(c => c.ParentId == null).ToListAsync(ct);
        foreach (var name in Products.Select(p => p.Category).Distinct().Where(n => !existing.Any(c => string.Equals(c.Name, n, StringComparison.CurrentCultureIgnoreCase))))
        {
            var category = new Category { TenantId = tenantId, Name = name, SortOrder = existing.Count };
            db.Categories.Add(category);
            existing.Add(category);
        }
        await db.SaveChangesAsync(ct);
        return existing.ToDictionary(c => c.Name, StringComparer.CurrentCultureIgnoreCase);
    }

    private async Task<int> EnsureProductsAsync(Guid tenantId, Dictionary<string, Category> categories, CancellationToken ct)
    {
        var present = (await db.Products.Where(p => p.Barcode.StartsWith(BarcodePrefix)).Select(p => p.Barcode).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < Products.Length; i++)
        {
            var barcode = $"{BarcodePrefix}{i + 1:000}";
            if (present.Contains(barcode)) continue;
            var p = Products[i];
            db.Products.Add(new Product
            {
                TenantId = tenantId, Barcode = barcode, Name = p.Name, Brand = p.Brand, CategoryId = categories[p.Category].Id,
                Price = p.Price, VatRate = 20, Stock = p.Stock, B2BVisible = true, B2CVisible = true, B2CSalePrice = i == 1 ? 179.90m : null, MinOrderQuantity = p.Min, PackSize = p.Pack,
            });
        }
        await db.SaveChangesAsync(ct);
        return await db.Products.CountAsync(p => p.Barcode.StartsWith(BarcodePrefix), ct);
    }

    private async Task<int> EnsureDealerAsync(Guid tenantId, CancellationToken ct)
    {
        var email = (config["Seed:DemoDealerEmail"] ?? "bayi@demo.local").Trim().ToLowerInvariant();
        var password = config["Seed:DemoDealerPassword"];
        if (string.IsNullOrEmpty(password)) return await db.Dealers.CountAsync(ct); // parola yoksa bayi oluşturulmaz

        const string taxNumber = "1000000099";
        var group = await db.DealerGroups.FirstOrDefaultAsync(g => g.Name == "Demo Bayiler", ct);
        if (group is null) db.DealerGroups.Add(group = new DealerGroup { TenantId = tenantId, Name = "Demo Bayiler", Description = "Geliştirme için demo bayi grubu" });
        var dealer = await db.Dealers.FirstOrDefaultAsync(d => d.TaxNumber == taxNumber, ct);
        if (dealer is null)
        {
            db.Dealers.Add(dealer = new Dealer
            {
                TenantId = tenantId, Name = "Demo Ticaret Ltd.", TaxNumber = taxNumber, TaxOffice = "Kadıköy", ContactName = "Demo Bayi", Email = email,
                Phone = "0555 000 00 00", City = "İstanbul", Address = "Demo Mah. Örnek Sok. No:1 Kadıköy / İstanbul",
                Status = DealerStatus.Active, ApprovedAt = DateTime.UtcNow, GroupId = group.Id,
            });
        }
        if (!await db.DealerUsers.AnyAsync(u => u.Email == email, ct))
        {
            var user = new DealerUser { TenantId = tenantId, DealerId = dealer.Id, Email = email, FullName = "Demo Bayi", Role = DealerUserRole.Owner };
            user.PasswordHash = new PasswordHasher<DealerUser>().HashPassword(user, password);
            db.DealerUsers.Add(user);
        }
        await db.SaveChangesAsync(ct);
        return await db.Dealers.CountAsync(ct);
    }

    /// <summary>B2C mağaza demo müşterisi (adres defteri ve üç sipariş: teslim edildi, hazırlanıyor, yeni); parola yoksa oluşturulmaz.</summary>
    private async Task<int> EnsureCustomerAsync(Guid tenantId, CancellationToken ct)
    {
        var email = (config["Seed:DemoCustomerEmail"] ?? "musteri@demo.local").Trim().ToLowerInvariant();
        var password = config["Seed:DemoCustomerPassword"];
        if (string.IsNullOrEmpty(password)) return await db.Customers.CountAsync(ct);

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Email == email, ct);
        if (customer is null)
        {
            customer = new Customer { TenantId = tenantId, Email = email, FullName = "Demo Müşteri", Phone = "0555 111 22 33", EmailVerified = true, TermsAcceptedAt = DateTime.UtcNow };
            customer.PasswordHash = new PasswordHasher<Customer>().HashPassword(customer, password);
            db.Customers.Add(customer);
            db.CustomerAddresses.Add(new CustomerAddress
            {
                TenantId = tenantId, CustomerId = customer.Id, Title = "Ev", FullName = "Demo Müşteri", Phone = "0555 111 22 33", City = "İstanbul", District = "Kadıköy",
                AddressLine = "Moda Cad. No:5 Daire:3", PostalCode = "34710", IsDefault = true,
            });
            await db.SaveChangesAsync(ct);
        }

        var products = await db.Products.AsNoTracking().Where(p => p.Barcode.StartsWith(BarcodePrefix)).OrderBy(p => p.Barcode).Take(4).ToListAsync(ct);
        (string Number, OrderStatus Status, int DaysAgo, int[] Items)[] orders =
        [
            ("B2C-DEMO-001", OrderStatus.Delivered, 21, [0, 2]),
            ("B2C-DEMO-002", OrderStatus.Preparing, 3, [1]),
            ("B2C-DEMO-003", OrderStatus.New, 0, [3, 0]),
        ];
        foreach (var (number, status, daysAgo, items) in orders)
        {
            if (products.Count <= items.Max() || await db.Orders.AnyAsync(o => o.Channel == "B2C" && o.OrderNumber == number, ct)) continue;
            var date = DateTime.UtcNow.AddDays(-daysAgo);
            var lines = items.Select((idx, n) => new OrderLine
            {
                TenantId = tenantId, LineId = (n + 1).ToString(), Barcode = products[idx].Barcode, Name = products[idx].Name, Quantity = n + 1, Price = products[idx].Price,
                PriceIncludesVat = true, VatRate = 20, ProductId = products[idx].Id,
            }).ToList();
            var total = lines.Sum(l => l.Price * l.Quantity);
            db.Orders.Add(new Order
            {
                TenantId = tenantId, Channel = "B2C", OrderNumber = number, Status = status, OrderDate = date, LastModifiedAt = date, CustomerId = customer.Id, CustomerName = customer.FullName,
                TotalPrice = total, TotalNet = decimal.Round(total / 1.2m, 2), Currency = "TRY", ShippingAddress = "Demo Müşteri · Moda Cad. No:5 Daire:3, Kadıköy / İstanbul",
                ShipmentCarrier = status == OrderStatus.Delivered ? "Yurtiçi Kargo" : null, TrackingNumber = status == OrderStatus.Delivered ? "YK123456789" : null, ShippedAt = status == OrderStatus.Delivered ? date.AddDays(1) : null,
                Packages = [new OrderPackage { TenantId = tenantId, Channel = "B2C", PackageId = number, ChannelStatus = status.ToString(), Status = status, LastModifiedAt = date, TotalPrice = total, Lines = lines }],
            });
        }
        await db.SaveChangesAsync(ct);
        return await db.Customers.CountAsync(ct);
    }

    /// <summary>Demo siteler için gereken modülleri açar (kiracının mevcut seçimi bozulmaz, yalnızca eksikler açılır). B2C mağaza hazır olana dek kiracılar bunu Ayarlar'dan açamaz; geliştirme için doğrudan açılır.</summary>
    private async Task EnsureModulesAsync(Guid tenantId, CancellationToken ct)
    {
        var rows = await db.TenantModules.ToListAsync(ct);
        foreach (var id in new[] { ModuleIds.Orders, ModuleIds.Dealers, ModuleIds.PriceLists, ModuleIds.B2BStore, ModuleIds.B2CStore })
        {
            var row = rows.FirstOrDefault(r => r.ModuleId == id);
            if (row is null) db.TenantModules.Add(new TenantModule { TenantId = tenantId, ModuleId = id, Enabled = true });
            else row.Enabled = true;
        }
        await db.SaveChangesAsync(ct);
    }
}
