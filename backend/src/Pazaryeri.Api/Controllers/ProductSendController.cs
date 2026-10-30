using Microsoft.AspNetCore.Mvc;
using Pazaryeri.Application.Products;

namespace Pazaryeri.Api.Controllers;

public record SendProductsInput(string? Channel, List<Guid>? ProductIds);

[ApiController]
[Route("api/products")]
public class ProductSendController(IProductSendService send) : ControllerBase
{
    /// <summary>Seçili ürünleri kanala göndermek üzere kuyruğa alır; sonuç ürünlerin kanal durumuna sonradan yansır.</summary>
    [HttpPost("send")]
    public async Task<ActionResult<ProductSendResult>> Send(SendProductsInput input, CancellationToken ct)
    {
        try
        {
            return await send.EnqueueAsync(input.Channel ?? string.Empty, input.ProductIds ?? [], ct);
        }
        catch (CatalogException e)
        {
            return StatusCode(e.Status, new { error = e.Message });
        }
    }
}
