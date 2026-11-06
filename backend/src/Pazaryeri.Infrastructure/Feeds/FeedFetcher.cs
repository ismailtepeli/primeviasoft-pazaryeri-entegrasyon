using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using Pazaryeri.Application.Feeds;

namespace Pazaryeri.Infrastructure.Feeds;

public class FeedFetcherOptions
{
    public const string Section = "ProductFeeds";
    /// <summary>Yerel/özel ağ adreslerine bağlanmaya izin verir. Yalnızca geliştirme ve testte açılır; üretimde kapalı olmalı (SSRF koruması).</summary>
    public bool AllowPrivateNetwork { get; set; }
    public long MaxBytes { get; set; } = 150L * 1024 * 1024;
    public int TimeoutSeconds { get; set; } = 120;
}

/// <summary>
/// Besleme adresini güvenle indirir. Kullanıcının verdiği adres sunucudan istendiği için (SSRF) her bağlantı, yönlendirmeler dahil, DNS çözümünden sonra
/// kontrol edilir: loopback, özel ağ, link-local (bulut metadata 169.254.x.x) ve benzeri adreslere bağlanılmaz. Boyut ve süre sınırlıdır; gzip açılır.
/// </summary>
public sealed class FeedFetcher
{
    private readonly FeedFetcherOptions _options;
    private readonly HttpClient _http;

    public FeedFetcher(IOptions<FeedFetcherOptions> options)
    {
        _options = options.Value;
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All, AllowAutoRedirect = true, MaxAutomaticRedirections = 3, UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(15), ConnectCallback = ConnectAsync,
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(Math.Max(10, _options.TimeoutSeconds)) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("PazaryeriFeedBot/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/xml, text/xml, */*");
    }

    /// <summary>Adres kurallarını denetler: http/https, kullanıcı bilgisi yok, makul uzunluk.</summary>
    public static Uri ValidateUrl(string? url)
    {
        var text = url?.Trim() ?? "";
        if (text.Length == 0) throw new FeedException(400, "Besleme adresi zorunlu.");
        if (text.Length > Pazaryeri.Domain.ProductFeed.MaxUrlLength) throw new FeedException(400, "Besleme adresi çok uzun.");
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) throw new FeedException(400, "Adres http:// ya da https:// ile başlamalı.");
        if (!string.IsNullOrEmpty(uri.UserInfo)) throw new FeedException(400, "Adres kullanıcı adı/parola içeremez; anahtar gerekiyorsa adresin sorgu kısmında verin.");
        return uri;
    }

    /// <summary>Halka açık bir internet adresi mi (özel, yerel, link-local, çok noktaya yayın vb. değil).</summary>
    public static bool IsPublic(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.None)) return false;
        var b = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            return !(b[0] == 0 || b[0] == 10 || b[0] >= 224
                || (b[0] == 100 && b[1] is >= 64 and <= 127) || (b[0] == 169 && b[1] == 254) || (b[0] == 172 && b[1] is >= 16 and <= 31)
                || (b[0] == 192 && b[1] == 168) || (b[0] == 192 && b[1] == 0 && b[2] == 0) || (b[0] == 198 && b[1] is 18 or 19));
        }
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            return !(ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast || (b[0] & 0xFE) == 0xFC || ip.IsIPv6Teredo);
        return false;
    }

    private async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, ct);
        var allowed = addresses.Where(a => _options.AllowPrivateNetwork || IsPublic(a)).ToArray();
        if (allowed.Length == 0) throw new FeedException(400, "Bu adres güvenlik nedeniyle kullanılamaz (yerel ya da özel ağ adresi).");
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch { socket.Dispose(); throw; }
    }

    /// <summary>Beslemeyi geçici bir dosyaya indirir; dosya kapatılınca silinir. Dönen akış aranabilirdir.</summary>
    public async Task<Stream> DownloadAsync(string url, CancellationToken ct)
    {
        var uri = ValidateUrl(url);
        FileStream? file = null;
        try
        {
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) throw new FeedException(502, $"Tedarikçi adresi {(int)response.StatusCode} döndürdü; adresi ve anahtarı kontrol edin.");
            if (response.Content.Headers.ContentLength > _options.MaxBytes) throw TooBig();

            file = NewTemp();
            await using (var body = await response.Content.ReadAsStreamAsync(ct)) await CopyLimitedAsync(body, file, ct);
            file.Position = 0;
            if (file.Length == 0) throw new FeedException(422, "Adres boş yanıt döndürdü.");

            // Sunucu gzip'i "Content-Encoding" olmadan, .gz dosyası olarak verebilir.
            var magic = new byte[2];
            var read = await file.ReadAsync(magic, ct);
            file.Position = 0;
            if (read == 2 && magic[0] == 0x1F && magic[1] == 0x8B)
            {
                var plain = NewTemp();
                try
                {
                    await using var gz = new GZipStream(file, CompressionMode.Decompress, leaveOpen: false);
                    await CopyLimitedAsync(gz, plain, ct);
                    plain.Position = 0;
                    file = null;
                    return plain;
                }
                catch { await plain.DisposeAsync(); throw; }
            }
            var result = file;
            file = null;
            return result;
        }
        catch (FeedException) { throw; }
        catch (HttpRequestException e) when (Unwrap(e) is { } fe) { throw fe; }
        catch (HttpRequestException) { throw new FeedException(502, "Tedarikçi adresine ulaşılamadı."); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new FeedException(504, "Tedarikçi adresi zamanında yanıt vermedi."); }
        catch (InvalidDataException) { throw new FeedException(422, "Sıkıştırılmış dosya açılamadı."); }
        finally { if (file is not null) await file.DisposeAsync(); }
    }

    private static FeedException? Unwrap(Exception e)
    {
        for (Exception? x = e; x is not null; x = x.InnerException) if (x is FeedException f) return f;
        return null;
    }

    private static FileStream NewTemp() =>
        new(Path.Combine(Path.GetTempPath(), "feed-" + Guid.NewGuid().ToString("N") + ".tmp"), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose);

    private FeedException TooBig() => new(413, $"Besleme çok büyük (en fazla {_options.MaxBytes / (1024 * 1024)} MB).");

    private async Task CopyLimitedAsync(Stream from, Stream to, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int n;
        while ((n = await from.ReadAsync(buffer, ct)) > 0)
        {
            total += n;
            if (total > _options.MaxBytes) throw TooBig();
            await to.WriteAsync(buffer.AsMemory(0, n), ct);
        }
    }
}
