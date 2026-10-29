using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Domain;
using Pazaryeri.Domain.Common;

namespace Pazaryeri.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options, ITenantProvider tenantProvider) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();

    // Filtre ifadesi bu özelliğe bağlanır; EF her sorguda geçerli context örneğinin değerini okur.
    // Oturum yoksa Guid.Empty olur ve hiçbir kayıt eşleşmez.
    private Guid CurrentTenantId => tenantProvider.TenantId ?? Guid.Empty;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Tenant>().HasIndex(t => t.Name).IsUnique();

        builder.Entity<User>(b =>
        {
            b.Property(u => u.Email).HasMaxLength(256).IsRequired();
            b.Property(u => u.FullName).HasMaxLength(200).IsRequired();
            b.HasIndex(u => u.Email).IsUnique(); // giriş e-postayla yapıldığı için tüm kiracılarda tekil
            b.HasOne<Tenant>().WithMany().HasForeignKey(u => u.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        // TenantId taşıyan her varlığa otomatik global filtre.
        foreach (var type in builder.Model.GetEntityTypes().Where(t => typeof(ITenantEntity).IsAssignableFrom(t.ClrType)))
        {
            var param = Expression.Parameter(type.ClrType, "e");
            var body = Expression.Equal(
                Expression.Property(param, nameof(ITenantEntity.TenantId)),
                Expression.Property(Expression.Constant(this), nameof(CurrentTenantId)));
            builder.Entity(type.ClrType).HasQueryFilter(Expression.Lambda(body, param));
            builder.Entity(type.ClrType).HasIndex(nameof(ITenantEntity.TenantId));
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var entry in ChangeTracker.Entries<ITenantEntity>().Where(e => e.State == EntityState.Added))
        {
            if (entry.Entity.TenantId != Guid.Empty) continue;
            entry.Entity.TenantId = tenantProvider.TenantId
                ?? throw new InvalidOperationException("Kiracı bilinmeden TenantId taşıyan kayıt eklenemez.");
        }
        return base.SaveChangesAsync(ct);
    }
}
