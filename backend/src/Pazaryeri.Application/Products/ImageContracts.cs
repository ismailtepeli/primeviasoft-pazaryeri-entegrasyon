namespace Pazaryeri.Application.Products;

/// <summary>Katalog işlemlerinde kullanıcıya gösterilecek iş kuralı hatası.</summary>
public class CatalogException(string message, int status = 400) : Exception(message)
{
    public int Status { get; } = status;
}

/// <summary>Yüklenen görsel dosyalarının saklandığı yer (yerelde disk; üretimde nesne depolama ile değiştirilebilir).</summary>
public interface IImageStore
{
    Task SaveAsync(Guid tenantId, Guid imageId, string extension, Stream content, CancellationToken ct = default);
    /// <summary>Dosya yoksa null.</summary>
    Stream? Open(Guid tenantId, Guid imageId, string extension);
    void Delete(Guid tenantId, Guid imageId, string extension);
}

public record ProductImageDto(Guid Id, string Url, int Position);

public interface IProductImageService
{
    Task<IReadOnlyList<ProductImageDto>> AddUploadsAsync(Guid productId, IReadOnlyList<(string FileName, Stream Content)> files, CancellationToken ct = default);
    Task<IReadOnlyList<ProductImageDto>> AddUrlAsync(Guid productId, string url, CancellationToken ct = default);
    /// <summary>Verilen sıraya göre yeniden numaralandırır; liste ürünün tüm görsellerini içermelidir.</summary>
    Task<IReadOnlyList<ProductImageDto>> ReorderAsync(Guid productId, IReadOnlyList<Guid> orderedIds, CancellationToken ct = default);
    Task<IReadOnlyList<ProductImageDto>> DeleteAsync(Guid productId, Guid imageId, CancellationToken ct = default);
}

/// <summary>Dosya başlığına bakarak görselin gerçek türünü belirler; istemcinin söylediği tür ya da uzantıya güvenilmez.</summary>
public static class ImageSniffer
{
    public const long MaxBytes = 5 * 1024 * 1024;

    /// <summary>jpg/png/webp ise (uzantı, içerik türü); değilse null.</summary>
    public static (string Extension, string ContentType)? Detect(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) return (".jpg", "image/jpeg");
        if (head.Length >= 8 && head[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return (".png", "image/png");
        if (head.Length >= 12 && head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8)) return (".webp", "image/webp");
        return null;
    }

    public static string ContentTypeFor(string extension) => extension switch
    {
        ".jpg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "application/octet-stream",
    };
}
