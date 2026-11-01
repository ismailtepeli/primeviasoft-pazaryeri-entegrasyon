using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Api.Controllers;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Domain.Common;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Tests;

/// <summary>#55 güvenlik gözden geçirmesinin kalıcı bekçileri: yeni anonim uç nokta ya da kiracısız tablo bilinçli onay olmadan eklenemez.</summary>
public class SecurityReviewTests
{
    private static IEnumerable<Type> Controllers() =>
        typeof(ModulesController).Assembly.GetTypes().Where(t => !t.IsAbstract && t.GetCustomAttribute<ApiControllerAttribute>() is not null);

    private static IEnumerable<MethodInfo> Actions(Type c) =>
        c.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => m.GetCustomAttributes<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().Any());

    private static bool Anonymous(Type c, MethodInfo a) =>
        c.GetCustomAttribute<AllowAnonymousAttribute>() is not null || a.GetCustomAttribute<AllowAnonymousAttribute>() is not null;

    private static bool RateLimited(Type c, MethodInfo a) =>
        c.GetCustomAttribute<EnableRateLimitingAttribute>() is not null || a.GetCustomAttribute<EnableRateLimitingAttribute>() is not null;

    [Fact]
    public void Anonim_uc_noktalar_yalnizca_onaylanmis_listedir()
    {
        var anonymous = Controllers().SelectMany(c => Actions(c).Where(a => Anonymous(c, a)).Select(a => $"{c.Name}.{a.Name}")).OrderBy(x => x).ToList();
        var expected = new[]
        {
            "AmazonOAuthController.Callback",        // Amazon'un yönlendirdiği adres; kiracı state'ten
            "AuthController.Login",                  // yönetici girişi
            "ImagesController.Get",                  // tahmin edilemez Guid, salt okunur görsel
            "StoreAuthController.Apply", "StoreAuthController.ForgotPassword", "StoreAuthController.Info",
            "StoreAuthController.Login", "StoreAuthController.ResetPassword", // bayi sitesi kimlik doğrulaması
            "WebhooksController.Receive",            // gizli anahtarla doğrulanan pazaryeri bildirimi
        }.OrderBy(x => x).ToList();
        Assert.Equal(expected, anonymous);
    }

    [Fact]
    public void Gorsel_disindaki_her_anonim_uc_nokta_hiz_sinirlidir()
    {
        var open = Controllers().SelectMany(c => Actions(c).Where(a => Anonymous(c, a) && !RateLimited(c, a)).Select(a => $"{c.Name}.{a.Name}")).ToList();
        Assert.Equal(["ImagesController.Get"], open);
    }

    [Fact]
    public void Kiraciya_bagli_her_tablo_kiraci_filtresine_sahiptir_ve_kiracisiz_tablolar_onayli_listedir()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=Pazaryeri_Model_Only;Trusted_Connection=True").Options, new NoTenant());
        var tenantScoped = db.Model.GetEntityTypes().Where(t => typeof(ITenantEntity).IsAssignableFrom(t.ClrType)).ToList();
        Assert.NotEmpty(tenantScoped);
        Assert.Empty(tenantScoped.Where(t => t.GetDeclaredQueryFilters().Count == 0).Select(t => t.ClrType.Name));

        // Kiracısız tablolar: kiracının kendisi, kiracıdan bağımsız paylaşımlı kanal kataloğu ve kimlik yardımcıları. Yeni biri eklenirse bilerek buraya yazılmalı.
        var shared = db.Model.GetEntityTypes().Where(t => !typeof(ITenantEntity).IsAssignableFrom(t.ClrType) && !t.IsOwned()).Select(t => t.ClrType.Name).OrderBy(x => x).ToList();
        Assert.Equal(ExpectedSharedTables, shared);
    }

    private static readonly string[] ExpectedSharedTables = ["CatalogSyncRun", "ChannelAttribute", "ChannelAttributeValue", "ChannelBrand", "ChannelCategory", "Tenant"];

    private sealed class NoTenant : ITenantProvider
    {
        public Guid? TenantId => null;
    }
}
