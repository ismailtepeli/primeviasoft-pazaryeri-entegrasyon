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
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductListing> ProductListings => Set<ProductListing>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<ProductChannelData> ProductChannelData => Set<ProductChannelData>();

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
            b.HasIndex(j => new { j.BatchPending, j.BatchSentAt });
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

        builder.Entity<Product>(b =>
        {
            b.Property(p => p.Name).HasMaxLength(500).IsRequired();
            b.Property(p => p.Barcode).HasMaxLength(100).IsRequired();
            b.Property(p => p.MainId).HasMaxLength(100);
            b.Property(p => p.Brand).HasMaxLength(200);
            b.Property(p => p.Description).HasMaxLength(5000);
            b.HasOne<Category>().WithMany().HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(p => p.Images).WithOne().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Cascade);
            b.Property(p => p.Variant).HasMaxLength(500);
            b.HasIndex(p => new { p.TenantId, p.MainId });
            b.Property(p => p.Price).HasPrecision(18, 2);
            b.HasIndex(p => new { p.TenantId, p.Barcode }).IsUnique(); // barkod kiracı içinde tekil
            b.HasOne<Tenant>().WithMany().HasForeignKey(p => p.TenantId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(p => p.Listings).WithOne().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ProductImage>(b =>
        {
            b.Property(i => i.Kind).HasConversion<string>().HasMaxLength(20);
            b.Property(i => i.Extension).HasMaxLength(10);
            b.Property(i => i.ExternalUrl).HasMaxLength(2000);
            b.HasIndex(i => new { i.ProductId, i.Position });
            b.HasOne<Tenant>().WithMany().HasForeignKey(i => i.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ProductChannelData>(b =>
        {
            b.ToTable("ProductChannelData");
            b.Property(d => d.Channel).HasMaxLength(40).IsRequired();
            b.Property(d => d.DimensionalWeight).HasPrecision(18, 3);
            b.HasIndex(d => new { d.ProductId, d.Channel }).IsUnique();
            b.HasOne<Product>().WithMany().HasForeignKey(d => d.ProductId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<Tenant>().WithMany().HasForeignKey(d => d.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Category>(b =>
        {
            b.Property(c => c.Name).HasMaxLength(200).IsRequired();
            b.HasIndex(c => new { c.TenantId, c.ParentId, c.Name }).IsUnique(); // aynı üst altında aynı ad olmaz
            b.HasOne<Category>().WithMany().HasForeignKey(c => c.ParentId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ProductListing>(b =>
        {
            b.Property(l => l.Channel).HasMaxLength(40).IsRequired();
            b.Property(l => l.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(l => l.ErrorMessage).HasMaxLength(2000);
            b.HasIndex(l => new { l.ProductId, l.Channel }).IsUnique();
            b.HasOne<Tenant>().WithMany().HasForeignKey(l => l.TenantId).OnDelete(DeleteBehavior.Restrict);
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
