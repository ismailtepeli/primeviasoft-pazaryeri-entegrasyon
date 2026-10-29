using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Pazaryeri.Domain;

namespace Pazaryeri.Infrastructure.Persistence;

/// <summary>Geliştirme ortamında ilk kiracıyı ve yöneticiyi oluşturur. Bilgiler appsettings "Seed" bölümünden gelir.</summary>
public class DevDataSeeder(AppDbContext db, IConfiguration config)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        var tenantName = config["Seed:TenantName"];
        var email = config["Seed:AdminEmail"]?.Trim().ToLowerInvariant();
        var password = config["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password)) return;

        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email, ct)) return;

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Name == tenantName, ct);
        if (tenant is null)
        {
            tenant = new Tenant { Name = tenantName };
            db.Tenants.Add(tenant);
        }

        var user = new User { TenantId = tenant.Id, Email = email, FullName = "Yönetici" };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
    }
}
