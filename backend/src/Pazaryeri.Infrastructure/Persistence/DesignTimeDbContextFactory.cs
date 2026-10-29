using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pazaryeri.Application.Abstractions;

namespace Pazaryeri.Infrastructure.Persistence;

/// <summary>`dotnet ef` komutları için; çalışma zamanında kullanılmaz.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        return new AppDbContext(options, new NoTenant());
    }

    private sealed class NoTenant : ITenantProvider
    {
        public Guid? TenantId => null;
    }
}
