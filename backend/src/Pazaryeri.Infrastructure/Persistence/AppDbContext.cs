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
    public DbSet<SyncJob> SyncJobs => Set<SyncJob>();
    public DbSet<ApiCallLog> ApiCallLogs => Set<ApiCallLog>();
    public DbSet<ChannelConnection> ChannelConnections => Set<ChannelConnection>();
    public DbSet<ChannelCredential> ChannelCredentials => Set<ChannelCredential>();

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

        builder.Entity<SyncJob>(b =>
        {
            b.Property(j => j.Type).HasConversion<string>().HasMaxLength(40);
            b.Property(j => j.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(j => j.Channel).HasMaxLength(40).IsRequired();
            b.Property(j => j.BatchId).HasMaxLength(100);
            b.Property(j => j.Error).HasMaxLength(2000);
            b.HasIndex(j => new { j.TenantId, j.Status });
            b.HasIndex(j => j.BatchId);
            b.HasOne<Tenant>().WithMany().HasForeignKey(j => j.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ApiCallLog>(b =>
        {
            b.Property(l => l.Channel).HasMaxLength(40).IsRequired();
            b.Property(l => l.SellerId).HasMaxLength(100).IsRequired();
            b.Property(l => l.Method).HasMaxLength(10).IsRequired();
            b.Property(l => l.Url).HasMaxLength(2000).IsRequired();
            b.Property(l => l.Error).HasMaxLength(2000);
            b.HasIndex(l => new { l.TenantId, l.SellerId, l.CreatedAt });
            b.HasOne<Tenant>().WithMany().HasForeignKey(l => l.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ChannelConnection>(b =>
        {
            b.Property(c => c.Channel).HasMaxLength(40).IsRequired();
            b.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(c => c.Environment).HasConversion<string>().HasMaxLength(20);
            b.HasIndex(c => new { c.TenantId, c.Channel }).IsUnique(); // kiracı başına kanal başına tek bağlantı
            b.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ChannelCredential>(b =>
        {
            b.Property(c => c.Channel).HasMaxLength(40).IsRequired();
            b.Property(c => c.Environment).HasConversion<string>().HasMaxLength(20);
            b.Property(c => c.PublicFieldsJson).HasMaxLength(2000).IsRequired();
            b.Property(c => c.EncryptedSecrets).IsRequired();
            b.Property(c => c.SecretHintsJson).HasMaxLength(2000).IsRequired();
            b.HasIndex(c => new { c.TenantId, c.Channel, c.Environment }).IsUnique(); // canlı ve test ayrı satır
            b.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
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
