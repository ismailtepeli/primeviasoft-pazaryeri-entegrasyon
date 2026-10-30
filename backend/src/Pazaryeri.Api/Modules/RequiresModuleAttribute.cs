using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Modules;

namespace Pazaryeri.Api.Modules;

/// <summary>
/// Denetleyiciyi ya da eylemi bir modüle bağlar: modül kiracı için kapalıysa 403 ve <c>module_disabled</c> döner
/// (kimlik doğrulama hatasından ayırt edilebilsin diye 404 değil). Oturumsuz isteğe karışmaz; onu yetkilendirme karşılar.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequiresModuleAttribute(string moduleId) : Attribute, IAsyncActionFilter
{
    public string ModuleId { get; } = moduleId;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var tenant = context.HttpContext.RequestServices.GetRequiredService<ITenantProvider>();
        if (tenant.TenantId is { } tenantId)
        {
            var modules = context.HttpContext.RequestServices.GetRequiredService<IModuleService>();
            if (!await modules.IsEnabledAsync(ModuleId, tenantId, context.HttpContext.RequestAborted))
            {
                context.Result = new ObjectResult(new { error = "module_disabled", module = ModuleId, message = "Bu modül kapalı; Ayarlar > Modüller'den açabilirsiniz." }) { StatusCode = StatusCodes.Status403Forbidden };
                return;
            }
        }
        await next();
    }
}

/// <summary>Çekirdek modüle ait olduğunu belirtir (kapatılamaz, koruma gerekmez); yalnızca kapsam denetimi içindir.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class CoreModuleAttribute : Attribute;
