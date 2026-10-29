using Pazaryeri.Application.Abstractions;
using Pazaryeri.Infrastructure.Auth;

namespace Pazaryeri.Api;

/// <summary>Kiracıyı JWT içindeki tenant_id claim'inden, HTTP dışı işlerde AmbientTenant'tan okur.</summary>
public class CurrentTenantProvider(IHttpContextAccessor accessor) : ITenantProvider
{
    public Guid? TenantId =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirst(AppClaims.TenantId)?.Value, out var id) ? id : AmbientTenant.TenantId;
}
