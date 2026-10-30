using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Pazaryeri.Api.Controllers;
using Pazaryeri.Api.Modules;
using Pazaryeri.Application.Modules;

namespace Pazaryeri.Tests;

/// <summary>ADR 0001: her API uç noktası bir modüle ya da çekirdeğe atanmalı; atanmayan uç nokta korumasız kalır.</summary>
public class ModuleCoverageTests
{
    private static IEnumerable<Type> Controllers() =>
        typeof(ModulesController).Assembly.GetTypes().Where(t => !t.IsAbstract && t.GetCustomAttribute<ApiControllerAttribute>() is not null);

    private static IEnumerable<MethodInfo> Actions(Type controller) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any());

    private static bool Marked(ICustomAttributeProvider p) =>
        p.GetCustomAttributes(typeof(RequiresModuleAttribute), false).Length > 0 || p.GetCustomAttributes(typeof(CoreModuleAttribute), false).Length > 0;

    [Fact]
    public void Her_denetleyici_ya_da_butun_eylemleri_bir_module_atanmistir()
    {
        var unassigned = new List<string>();
        foreach (var controller in Controllers())
        {
            if (Marked(controller)) continue;
            unassigned.AddRange(Actions(controller).Where(a => !Marked(a)).Select(a => $"{controller.Name}.{a.Name}"));
        }
        Assert.Empty(unassigned);
    }

    [Fact]
    public void RequiresModule_yalnizca_katalogdaki_modul_kimliklerini_kullanir()
    {
        var ids = ModuleCatalog.All.Select(m => m.Id).ToHashSet();
        var used = Controllers().SelectMany(c => new ICustomAttributeProvider[] { c }.Concat(Actions(c)))
            .SelectMany(p => p.GetCustomAttributes(typeof(RequiresModuleAttribute), false).Cast<RequiresModuleAttribute>()).Select(a => a.ModuleId).ToList();

        Assert.NotEmpty(used);
        Assert.All(used, id => Assert.Contains(id, ids));
    }

    [Theory]
    [InlineData(typeof(OrdersController), "orders")]
    [InlineData(typeof(PricingController), "stock-price")]
    [InlineData(typeof(StockSyncController), "stock-price")]
    [InlineData(typeof(CatalogController), "marketplace")]
    [InlineData(typeof(ProductSendController), "marketplace")]
    [InlineData(typeof(ChannelCredentialsController), "marketplace")]
    public void Modul_eslemesi_ADR_ile_ayni_dir(Type controller, string module) =>
        Assert.Equal(module, controller.GetCustomAttribute<RequiresModuleAttribute>()?.ModuleId);

    [Fact]
    public void Karisik_denetleyicilerde_eylemler_ayrilir()
    {
        string? ModuleOf(Type c, string action) => Actions(c).Single(a => a.Name == action).GetCustomAttribute<RequiresModuleAttribute>()?.ModuleId;

        Assert.Equal("stock-price", ModuleOf(typeof(StockController), nameof(StockController.Rules)));
        Assert.Null(ModuleOf(typeof(StockController), nameof(StockController.Movements))); // stok hareketleri çekirdek
        Assert.Equal("marketplace", ModuleOf(typeof(DashboardController), nameof(DashboardController.Channels)));
        Assert.Null(ModuleOf(typeof(DashboardController), nameof(DashboardController.Summary)));
    }
}
