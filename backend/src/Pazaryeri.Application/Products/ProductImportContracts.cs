namespace Pazaryeri.Application.Products;

/// <param name="Row">Excel'deki satır numarası (başlık 1. satır).</param>
public record ProductImportError(int Row, string Barcode, string Message);

/// <param name="Created">Yeni eklenen ürün sayısı.</param>
/// <param name="Updated">Barkodu zaten kayıtlı olup güncellenen ürün sayısı.</param>
/// <param name="Groups">İçe aktarılan ürünlerin kaç farklı ana kod (varyant grubu) altında toplandığı.</param>
public record ProductImportResult(int Created, int Updated, int Groups, IReadOnlyList<ProductImportError> Errors);

/// <summary>Dosyanın kendisi okunamadığında (biçim, boyut, başlık) fırlatılır; satır hataları sonuçta raporlanır.</summary>
public class ProductImportException(string message) : Exception(message);

public interface IProductImportService
{
    byte[] BuildTemplate();
    Task<ProductImportResult> ImportAsync(Stream xlsx, CancellationToken ct = default);
}
