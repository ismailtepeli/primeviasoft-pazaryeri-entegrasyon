using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Application.Products;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Api.Controllers;

public record AddImageUrlInput(string? Url);

public record ReorderImagesInput(List<Guid>? Ids);

[ApiController]
[Route("api/products/{productId:guid}/images")]
public class ProductImagesController(IProductImageService images) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(ProductImage.MaxPerProduct * (ImageSniffer.MaxBytes + 4096))]
    public Task<ActionResult<IReadOnlyList<ProductImageDto>>> Upload(Guid productId, List<IFormFile> files, CancellationToken ct) =>
        Run(async () =>
        {
            var opened = files.Select(f => (f.FileName, (Stream)f.OpenReadStream())).ToList();
            try { return await images.AddUploadsAsync(productId, opened, ct); }
            finally { foreach (var (_, s) in opened) await s.DisposeAsync(); }
        });

    [HttpPost("url")]
    public Task<ActionResult<IReadOnlyList<ProductImageDto>>> AddUrl(Guid productId, AddImageUrlInput input, CancellationToken ct) =>
        Run(() => images.AddUrlAsync(productId, input.Url ?? string.Empty, ct));

    [HttpPut("order")]
    public Task<ActionResult<IReadOnlyList<ProductImageDto>>> Reorder(Guid productId, ReorderImagesInput input, CancellationToken ct) =>
        Run(() => images.ReorderAsync(productId, input.Ids ?? [], ct));

    [HttpDelete("{imageId:guid}")]
    public Task<ActionResult<IReadOnlyList<ProductImageDto>>> Delete(Guid productId, Guid imageId, CancellationToken ct) =>
        Run(() => images.DeleteAsync(productId, imageId, ct));

    private async Task<ActionResult<IReadOnlyList<ProductImageDto>>> Run(Func<Task<IReadOnlyList<ProductImageDto>>> action)
    {
        try { return Ok(await action()); }
        catch (CatalogException e) { return StatusCode(e.Status, new { error = e.Message }); }
    }
}

/// <summary>
/// Yüklenmiş görselleri sunar. Tarayıcı &lt;img&gt; ve pazaryerleri oturum başlığı gönderemediği için anonimdir;
/// adres tahmin edilemeyen rastgele bir Guid'dir ve yalnızca görsel dosyası döner.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/images")]
public class ImagesController(AppDbContext db, IImageStore store) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var image = await db.ProductImages.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == id && i.Kind == ImageKind.Upload, ct);
        if (image is null) return NotFound();

        var stream = store.Open(image.TenantId, image.Id, image.Extension!);
        if (stream is null) return NotFound();

        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "public, max-age=86400";
        return File(stream, ImageSniffer.ContentTypeFor(image.Extension!));
    }
}
