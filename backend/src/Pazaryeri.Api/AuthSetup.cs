using Microsoft.AspNetCore.Authorization;
using Pazaryeri.Infrastructure.Auth;

namespace Pazaryeri.Api;

/// <summary>Yetkilendirme politikaları: yönetim API'si bayi token'ını reddeder, bayi uç noktaları yalnızca bayi token'ını kabul eder.</summary>
public static class AuthSetup
{
    public static void Configure(AuthorizationOptions o)
    {
        // Eski yönetici token'larında principal alanı yoktur; bayi olmayan her oturum yönetici sayılır.
        var admin = new AuthorizationPolicyBuilder().RequireAuthenticatedUser()
            .RequireAssertion(c => !c.User.HasClaim(AppClaims.Principal, AppClaims.PrincipalDealer)).Build();
        o.DefaultPolicy = admin;
        o.FallbackPolicy = admin;
        o.AddPolicy(AuthPolicies.Dealer, p => p.RequireAuthenticatedUser().RequireClaim(AppClaims.Principal, AppClaims.PrincipalDealer));
    }
}
