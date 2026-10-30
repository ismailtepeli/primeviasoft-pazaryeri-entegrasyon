using Pazaryeri.Application.Products;

namespace Pazaryeri.Infrastructure.Products;

/// <summary>Görselleri diskte {kök}/{kiracı}/{görselId}{uzantı} olarak saklar. Adlar yalnızca Guid ve beyaz listedeki uzantıdan oluşur.</summary>
public class LocalImageStore(string root) : IImageStore
{
    private static readonly HashSet<string> Allowed = [".jpg", ".png", ".webp"];

    private string PathFor(Guid tenantId, Guid imageId, string extension)
    {
        if (!Allowed.Contains(extension)) throw new ArgumentException("Desteklenmeyen görsel uzantısı.", nameof(extension));
        return Path.Combine(root, tenantId.ToString("N"), imageId.ToString("N") + extension);
    }

    public async Task SaveAsync(Guid tenantId, Guid imageId, string extension, Stream content, CancellationToken ct = default)
    {
        var path = PathFor(tenantId, imageId, extension);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = File.Create(path);
        await content.CopyToAsync(file, ct);
    }

    public Stream? Open(Guid tenantId, Guid imageId, string extension)
    {
        var path = PathFor(tenantId, imageId, extension);
        return File.Exists(path) ? File.OpenRead(path) : null;
    }

    public void Delete(Guid tenantId, Guid imageId, string extension)
    {
        var path = PathFor(tenantId, imageId, extension);
        if (File.Exists(path)) File.Delete(path);
    }
}
