using System.Text.Json.Nodes;

namespace Pazaryeri.Sandbox;

/// <summary>Sahte pazaryerinin bellekteki durumu. Tüm erişim <see cref="Gate"/> altında yapılır.</summary>
public sealed class SandboxState
{
    public readonly object Gate = new();

    public Dictionary<string, JsonObject> Products { get; } = [];
    public Dictionary<string, SandboxBatch> Batches { get; } = [];
    public List<SandboxOrder> Orders { get; } = [];
    public List<JsonObject> Webhooks { get; } = [];
    public List<string> Requests { get; } = [];

    /// <summary>Sıradaki bu kadar istek (sandbox uçları hariç) <see cref="FailStatus"/> koduyla başarısız olur.</summary>
    public int FailCount { get; set; }
    public int FailStatus { get; set; } = 500;
    public int? FailRetryAfterSeconds { get; set; }

    /// <summary>Yeni batch'ler, sonuç sorgusunda bu kadar kez IN_PROGRESS döner.</summary>
    public int PendingPolls { get; set; }

    public int NextId;
    public long NextLineId = 9_000_000;

    public void Reset()
    {
        Products.Clear(); Batches.Clear(); Orders.Clear(); Webhooks.Clear(); Requests.Clear();
        FailCount = 0; FailStatus = 500; FailRetryAfterSeconds = null; PendingPolls = 0;
    }

    public string NewId() => Guid.NewGuid().ToString("N")[..12] + "-" + Interlocked.Increment(ref NextId);
}

public sealed class SandboxBatch(string id, List<JsonObject> items, int pendingPolls)
{
    public string Id { get; } = id;
    public List<JsonObject> Items { get; } = items;
    public int PendingPolls { get; set; } = pendingPolls;
}

public sealed class SandboxLine(long id, string barcode, string name, int quantity, decimal amount)
{
    public long Id { get; } = id;
    public string Barcode { get; } = barcode;
    public string Name { get; } = name;
    public int Quantity { get; } = quantity;
    public decimal Amount { get; } = amount;
}

public sealed class SandboxOrder(string orderNumber, long packageId, DateTimeOffset orderDate, string customerFirst, string customerLast, List<SandboxLine> lines)
{
    public string OrderNumber { get; } = orderNumber;
    public long PackageId { get; } = packageId;
    public DateTimeOffset OrderDate { get; } = orderDate;
    public string CustomerFirstName { get; } = customerFirst;
    public string CustomerLastName { get; } = customerLast;
    public List<SandboxLine> Lines { get; } = lines;
    public string Status { get; set; } = "Created";
    public DateTimeOffset LastModified { get; set; } = orderDate;
    public decimal Total => Lines.Sum(l => l.Amount * l.Quantity);
}
