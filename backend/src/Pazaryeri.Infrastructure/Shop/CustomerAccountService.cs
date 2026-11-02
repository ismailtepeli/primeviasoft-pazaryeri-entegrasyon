using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Dealers;
using Pazaryeri.Application.Shop;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Infrastructure.Shop;

public class CustomerException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

/// <summary>
/// Oturum açmış müşterinin hesabı: profil, parola, adres defteri ve siparişleri. Her işlem token'daki müşteri kimliğiyle sınırlıdır;
/// başkasının adresi ya da siparişi hiçbir yoldan okunmaz/değiştirilmez (bulunamadı olarak yanıtlanır). Kiracı filtresi bağlamdan gelir.
/// </summary>
public class CustomerAccountService(AppDbContext db)
{
    public const string OrderChannel = "B2C";
    public const int OrderPageSize = 10;
    private readonly PasswordHasher<Customer> _hasher = new();

    private async Task<Customer> LoadAsync(Guid customerId, bool addresses, CancellationToken ct)
    {
        IQueryable<Customer> q = db.Customers;
        if (addresses) q = db.Customers.Include(c => c.Addresses);
        var c = await q.FirstOrDefaultAsync(x => x.Id == customerId && x.IsActive, ct);
        return c ?? throw new CustomerException(404, "Hesap bulunamadı.");
    }

    // ---------- profil ----------

    public async Task<CustomerProfile> GetProfileAsync(Guid customerId, CancellationToken ct) =>
        CustomerAuthService.ToProfile(await LoadAsync(customerId, false, ct));

    public async Task<CustomerProfile> UpdateProfileAsync(Guid customerId, ProfileUpdate update, CancellationToken ct)
    {
        if ((CustomerRules.ValidateName(update.FullName) ?? CustomerRules.ValidatePhone(update.Phone, required: false)) is { } error) throw new CustomerException(400, error);
        var c = await LoadAsync(customerId, false, ct);
        c.FullName = update.FullName!.Trim();
        c.Phone = DealerRules.Clean(update.Phone);
        c.MarketingConsent = update.MarketingConsent;
        await db.SaveChangesAsync(ct);
        return CustomerAuthService.ToProfile(c);
    }

    /// <summary>Parolayı değiştirir; hesapta parola yoksa (yalnızca Google) ilk parolayı belirler ve mevcut parola istemez.</summary>
    public async Task ChangePasswordAsync(Guid customerId, PasswordChange change, CancellationToken ct)
    {
        if (DealerRules.ValidatePassword(change.New) is { } error) throw new CustomerException(400, error);
        var c = await LoadAsync(customerId, false, ct);
        if (!string.IsNullOrEmpty(c.PasswordHash)
            && (string.IsNullOrEmpty(change.Current) || _hasher.VerifyHashedPassword(c, c.PasswordHash, change.Current) == PasswordVerificationResult.Failed))
            throw new CustomerException(400, "Mevcut parola hatalı.");
        c.PasswordHash = _hasher.HashPassword(c, change.New!);
        await db.SaveChangesAsync(ct);
    }

    // ---------- adres defteri ----------

    private static AddressDto ToDto(CustomerAddress a) => new(a.Id, a.Title, a.FullName, a.Phone, a.City, a.District, a.AddressLine, a.PostalCode, a.IsDefault);

    public async Task<IReadOnlyList<AddressDto>> AddressesAsync(Guid customerId, CancellationToken ct) =>
        (await db.CustomerAddresses.AsNoTracking().Where(a => a.CustomerId == customerId).OrderByDescending(a => a.IsDefault).ThenBy(a => a.CreatedAt).ToListAsync(ct)).Select(ToDto).ToList();

    private static void Fill(CustomerAddress a, AddressInput i)
    {
        a.Title = i.Title!.Trim(); a.FullName = i.FullName!.Trim(); a.Phone = i.Phone!.Trim(); a.City = i.City!.Trim(); a.District = i.District!.Trim();
        a.AddressLine = i.AddressLine!.Trim(); a.PostalCode = DealerRules.Clean(i.PostalCode);
    }

    public async Task<AddressDto> AddAddressAsync(Guid customerId, AddressInput input, CancellationToken ct)
    {
        if (CustomerRules.ValidateAddress(input) is { } error) throw new CustomerException(400, error);
        var c = await LoadAsync(customerId, true, ct);
        if (c.Addresses.Count >= CustomerAddress.MaxPerCustomer) throw new CustomerException(409, $"En fazla {CustomerAddress.MaxPerCustomer} adres kaydedebilirsiniz.");
        var address = new CustomerAddress { TenantId = c.TenantId, CustomerId = c.Id };
        Fill(address, input);
        address.IsDefault = input.IsDefault || c.Addresses.Count == 0; // ilk adres varsayılan olur
        if (address.IsDefault) foreach (var other in c.Addresses) other.IsDefault = false;
        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync(ct);
        return ToDto(address);
    }

    public async Task<AddressDto> UpdateAddressAsync(Guid customerId, Guid addressId, AddressInput input, CancellationToken ct)
    {
        if (CustomerRules.ValidateAddress(input) is { } error) throw new CustomerException(400, error);
        var c = await LoadAsync(customerId, true, ct);
        var address = c.Addresses.FirstOrDefault(a => a.Id == addressId) ?? throw new CustomerException(404, "Adres bulunamadı.");
        Fill(address, input);
        if (input.IsDefault && !address.IsDefault)
        {
            foreach (var other in c.Addresses) other.IsDefault = false;
            address.IsDefault = true;
        }
        await db.SaveChangesAsync(ct);
        return ToDto(address);
    }

    public async Task DeleteAddressAsync(Guid customerId, Guid addressId, CancellationToken ct)
    {
        var c = await LoadAsync(customerId, true, ct);
        var address = c.Addresses.FirstOrDefault(a => a.Id == addressId) ?? throw new CustomerException(404, "Adres bulunamadı.");
        db.CustomerAddresses.Remove(address);
        if (address.IsDefault && c.Addresses.Where(a => a.Id != addressId).OrderBy(a => a.CreatedAt).FirstOrDefault() is { } next) next.IsDefault = true; // varsayılan silinirse en eski adres devralır
        await db.SaveChangesAsync(ct);
    }

    // ---------- siparişler ----------

    private IQueryable<Order> Mine(Guid customerId) => db.Orders.AsNoTracking().Where(o => o.CustomerId == customerId && o.Channel == OrderChannel);

    public async Task<CustomerOrderPage> OrdersAsync(Guid customerId, int page, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        var q = Mine(customerId);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(o => o.OrderDate).ThenByDescending(o => o.Id).Skip((page - 1) * OrderPageSize).Take(OrderPageSize)
            .Select(o => new
            {
                o.Id, o.OrderNumber, o.OrderDate, o.Status, o.TotalPrice, o.Currency,
                Lines = o.Packages.SelectMany(p => p.Lines).OrderBy(l => l.LineId).Select(l => new { l.Name, l.Quantity, l.ProductId }).ToList(),
            }).ToListAsync(ct);
        var firstProducts = rows.Select(r => r.Lines.FirstOrDefault()?.ProductId).OfType<Guid>().ToList();
        var covers = await Covers(firstProducts, ct);
        var items = rows.Select(r => new CustomerOrderRow(r.Id, r.OrderNumber, r.OrderDate, r.Status.ToString(), r.TotalPrice, r.Currency ?? "TRY", r.Lines.Sum(l => l.Quantity),
            r.Lines.FirstOrDefault()?.Name, r.Lines.FirstOrDefault()?.ProductId is { } pid ? covers.GetValueOrDefault(pid) : null)).ToList();
        return new CustomerOrderPage(items, total, OrderPageSize);
    }

    public async Task<CustomerOrderDetail> OrderAsync(Guid customerId, Guid orderId, CancellationToken ct)
    {
        var o = await Mine(customerId).Include(x => x.Packages).ThenInclude(p => p.Lines).FirstOrDefaultAsync(x => x.Id == orderId, ct)
            ?? throw new CustomerException(404, "Sipariş bulunamadı.");
        var lines = o.Packages.SelectMany(p => p.Lines).OrderBy(l => int.TryParse(l.LineId, out var n) ? n : 0).ToList();
        var covers = await Covers(lines.Select(l => l.ProductId).OfType<Guid>().Distinct().ToList(), ct);
        return new CustomerOrderDetail(o.Id, o.OrderNumber, o.OrderDate, o.Status.ToString(), o.TotalPrice, o.Currency ?? "TRY", o.ShippingAddress, o.Note,
            o.ShipmentCarrier, o.TrackingNumber, o.ShippedAt, o.StatusNote, CanCancel: false, // müşteri iptali sipariş akışıyla birlikte gelir (#98, #100)
            lines.Select(l => new CustomerOrderLine(l.ProductId, l.Name, l.Quantity, l.Price, l.ProductId is { } pid ? covers.GetValueOrDefault(pid) : null)).ToList());
    }

    private async Task<Dictionary<Guid, string?>> Covers(List<Guid> productIds, CancellationToken ct)
    {
        if (productIds.Count == 0) return [];
        var rows = await db.ProductImages.AsNoTracking().Where(i => productIds.Contains(i.ProductId) && i.Position == 0).Select(i => new { i.ProductId, i.Id, i.Kind, i.ExternalUrl }).ToListAsync(ct);
        return rows.ToDictionary(r => r.ProductId, r => (string?)(r.Kind == ImageKind.Url ? r.ExternalUrl : $"/api/images/{r.Id}"));
    }
}
