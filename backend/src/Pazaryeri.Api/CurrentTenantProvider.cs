using Pazaryeri.Application.Abstractions;
using Pazaryeri.Infrastructure.Auth;

namespace Pazaryeri.Api;

/// <summary>Kiracıyı JWT içindeki tenant_id claim'inden okur.</summary>
public class CurrentTenantProvider(IHttpContextAccessor accessor) : ITenantProvider
{
    public Guid? TenantId =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirst(AppClaims.TenantId)?.Value, out var id) ? id : null;
}
