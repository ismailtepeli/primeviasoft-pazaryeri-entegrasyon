using Pazaryeri.Domain.Common;

namespace Pazaryeri.Domain;

/// <summary>
/// Bayinin sunucuda saklanan sepeti (bayi firma başına tek; firmanın kullanıcıları ortak kullanır).
/// Sipariş verirken sepet satırları tek işlemde "sahiplenilir": satır sürümü (RowVersion) çakışırsa ikinci istek reddedilir,
/// böylece çift tıklama ya da eşzamanlı iki istek aynı sepetten iki sipariş üretmez.
/// </summary>
public class Cart : TenantEntity
{
    public Guid DealerId { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public byte[] RowVersion { get; set; } = [];
    public List<CartItem> Items { get; set; } = [];
}

public class CartItem : TenantEntity
{
    public Guid CartId { get; set; }
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
    /// <summary>Ürün sepete eklendiği/güncellendiği andaki birim fiyat (gösterilen); fiyat değişirse bayiye uyarı verilir.</summary>
    public decimal PriceAtAdd { get; set; }
}
