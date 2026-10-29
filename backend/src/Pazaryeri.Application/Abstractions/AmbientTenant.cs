namespace Pazaryeri.Application.Abstractions;

/// <summary>
/// HTTP isteği olmayan yerlerde (kuyruk işçisi, zamanlanmış iş) kiracıyı belirler.
/// `using (AmbientTenant.Use(id))` bloğu içindeki veritabanı sorguları o kiracıya göre filtrelenir.
/// </summary>
public static class AmbientTenant
{
    private static readonly AsyncLocal<Guid?> Current = new();

    public static Guid? TenantId => Current.Value;

    public static IDisposable Use(Guid tenantId)
    {
        var previous = Current.Value;
        Current.Value = tenantId;
        return new Restore(previous);
    }

    private sealed class Restore(Guid? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
