using Microsoft.AspNetCore.Authorization;
using Pazaryeri.Infrastructure.Auth;

namespace Pazaryeri.Api;

/// <summary>Yetkilendirme politikaları: yönetim API'si bayi ve müşteri token'ını reddeder; bayi uçları yalnızca bayi, müşteri uçları yalnızca müşteri token'ını kabul eder.</summary>
public static class AuthSetup
{
    public static void Configure(AuthorizationOptions o)
    {
        // Eski yönetici token'larında principal alanı yoktur; bayi ve müşteri olmayan her oturum yönetici sayılır.
        var admin = new AuthorizationPolicyBuilder().RequireAuthenticatedUser()
            .RequireAssertion(c => !c.User.HasClaim(AppClaims.Principal, AppClaims.PrincipalDealer) && !c.User.HasClaim(AppClaims.Principal, AppClaims.PrincipalCustomer)).Build();
        o.DefaultPolicy = admin;
        o.FallbackPolicy = admin;
        o.AddPolicy(AuthPolicies.Dealer, p => p.RequireAuthenticatedUser().RequireClaim(AppClaims.Principal, AppClaims.PrincipalDealer));
        o.AddPolicy(AuthPolicies.Customer, p => p.RequireAuthenticatedUser().RequireClaim(AppClaims.Principal, AppClaims.PrincipalCustomer));
    }
}
