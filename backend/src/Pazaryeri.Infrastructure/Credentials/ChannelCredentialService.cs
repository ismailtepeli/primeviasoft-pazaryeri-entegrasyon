using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Abstractions;
using Pazaryeri.Application.Credentials;
using Pazaryeri.Application.Marketplace;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Infrastructure.Credentials;

public class ChannelCredentialService(AppDbContext db, ITenantProvider tenant, ICredentialProtector protector) : IChannelCredentialService
{
    /// <summary>Entegratör (bizim) firma adı; kanalların User-Agent kuralı: alfanumerik, en fazla 30 karakter.</summary>
    public const string IntegratorName = "PrimeviaSoft";

    private Guid TenantId => tenant.TenantId ?? throw new InvalidOperationException("Kiracı bilinmeden API bilgisi okunamaz/yazılamaz.");

    private string ContextFor(string channel, ChannelEnvironment env) => $"{TenantId}:{channel}:{env}";

    public async Task<CredentialView> SaveAsync(string channel, ChannelEnvironment environment, IReadOnlyDictionary<string, string> fields, CancellationToken ct = default)
    {
        var spec = ChannelCredentialSpec.For(channel)
            ?? throw new CredentialValidationException($"'{channel}' kanalı API anahtarı ile bağlanmaz.");
        channel = spec.Channel;

        var unknown = fields.Keys.Where(k => !spec.AllFields.Contains(k)).ToList();
        if (unknown.Count > 0) throw new CredentialValidationException($"Bilinmeyen alan: {string.Join(", ", unknown)}.");

        var row = await db.ChannelCredentials.FirstOrDefaultAsync(c => c.Channel == channel && c.Environment == environment, ct);
        var context = ContextFor(channel, environment);
        var oldSecrets = row is { EncryptedSecrets.Length: > 0 }
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(protector.Unprotect(row.EncryptedSecrets, context))!
            : [];

        var publicValues = new Dictionary<string, string>();
        foreach (var name in spec.PublicFields)
        {
            var value = fields.GetValueOrDefault(name)?.Trim();
            if (string.IsNullOrEmpty(value)) throw new CredentialValidationException($"{name} zorunlu.");
            publicValues[name] = value;
        }

        // Boş bırakılan gizli alan, kayıtlı değer varsa korunur; böylece tek bir anahtar güncellenebilir.
        var secrets = new Dictionary<string, string>();
        foreach (var name in spec.SecretFields)
        {
            var value = fields.GetValueOrDefault(name)?.Trim();
            if (!string.IsNullOrEmpty(value)) secrets[name] = value;
            else if (oldSecrets.TryGetValue(name, out var kept)) secrets[name] = kept;
            else throw new CredentialValidationException($"{name} zorunlu.");
        }

        row ??= db.ChannelCredentials.Add(new ChannelCredential { TenantId = TenantId, Channel = channel, Environment = environment }).Entity;
        row.PublicFieldsJson = JsonSerializer.Serialize(publicValues);
        row.EncryptedSecrets = protector.Protect(JsonSerializer.Serialize(secrets), context);
        row.SecretHintsJson = JsonSerializer.Serialize(secrets.ToDictionary(s => s.Key, s => LastFour(s.Value)));
        row.UpdatedAt = DateTime.UtcNow;

        // Kaydedilen ortam, kanalın etkin ortamı olur. Bilgiler yeni olduğu için "doğrulanmadı" sayılır;
        // "Bağlı" yalnızca başarılı bir bağlantı testinden sonra yazılır.
        var connection = await db.ChannelConnections.FirstOrDefaultAsync(c => c.Channel == channel, ct)
            ?? db.ChannelConnections.Add(new ChannelConnection { TenantId = TenantId, Channel = channel }).Entity;
        connection.Environment = environment;
        connection.Status = ConnectionStatus.Unverified;

        await db.SaveChangesAsync(ct);
        return ToView(spec, row);
    }

    public async Task<CredentialView?> GetMaskedAsync(string channel, ChannelEnvironment environment, CancellationToken ct = default)
    {
        var spec = ChannelCredentialSpec.For(channel);
        if (spec is null) return null;
        var row = await db.ChannelCredentials.FirstOrDefaultAsync(c => c.Channel == spec.Channel && c.Environment == environment, ct);
        return row is null ? null : ToView(spec, row);
    }

    public async Task<MarketplaceCredentials?> GetForClientAsync(string channel, CancellationToken ct = default)
    {
        var spec = ChannelCredentialSpec.For(channel);
        if (spec is null) return null;
        var connection = await db.ChannelConnections.FirstOrDefaultAsync(c => c.Channel == spec.Channel, ct);
        if (connection is null) return null;
        var row = await db.ChannelCredentials.FirstOrDefaultAsync(c => c.Channel == spec.Channel && c.Environment == connection.Environment, ct);
        if (row is null) return null;

        var secrets = JsonSerializer.Deserialize<Dictionary<string, string>>(protector.Unprotect(row.EncryptedSecrets, ContextFor(spec.Channel, row.Environment)))!;
        var publics = JsonSerializer.Deserialize<Dictionary<string, string>>(row.PublicFieldsJson)!;
        string Field(string? name) => name is null ? "" : publics.GetValueOrDefault(name) ?? secrets.GetValueOrDefault(name) ?? "";

        return new MarketplaceCredentials(spec.Channel, Field(spec.SellerField), Field(spec.KeyField), Field(spec.SecretField),
            IntegratorName, row.Environment == ChannelEnvironment.Staging);
    }

    private static CredentialView ToView(ChannelCredentialSpec spec, ChannelCredential row)
    {
        var publics = JsonSerializer.Deserialize<Dictionary<string, string>>(row.PublicFieldsJson)!;
        var hints = JsonSerializer.Deserialize<Dictionary<string, string>>(row.SecretHintsJson)!;
        var fields = spec.PublicFields.Select(n => new CredentialFieldView(n, false, publics.GetValueOrDefault(n), null))
            .Concat(spec.SecretFields.Select(n => new CredentialFieldView(n, true, null, "••••" + hints.GetValueOrDefault(n, ""))))
            .ToList();
        return new CredentialView(spec.Channel, row.Environment.ToString(), fields);
    }

    /// <summary>Kısa değerlerde son 4 hane değeri fazla açığa çıkarır; 8 karakterden kısaysa hiç gösterilmez.</summary>
    private static string LastFour(string secret) => secret.Length >= 8 ? secret[^4..] : "";
}
