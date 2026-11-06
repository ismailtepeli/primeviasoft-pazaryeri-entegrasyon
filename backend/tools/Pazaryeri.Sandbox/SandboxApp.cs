using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace Pazaryeri.Sandbox;

/// <summary>
/// Trendyol entegrasyon API'sinin dokümana göre yazılmış sahte sürümü (gerçek hesap/sandbox gerekmez).
/// Bağımsız çalışır: istemci kodunu paylaşmaz, bu yüzden istemcinin dokümanı yanlış anlamasını da yakalar.
/// Kimlik kuralı: Basic apiKey:apiSecret ve "{sellerId} - {Firma}" User-Agent'ı; yoksa gerçek API gibi 401/403.
/// Yönetim uçları /__sandbox altındadır: hata enjekte etme, sipariş ekleme, webhook gönderme, sıfırlama.
/// </summary>
public static class SandboxApp
{
    private static readonly HashSet<string> WebhookStatuses = ["CREATED", "PICKING", "INVOICED", "SHIPPED", "CANCELLED", "DELIVERED", "UNDELIVERED", "RETURNED", "UNSUPPLIED", "AWAITING", "UNPACKED", "AT_COLLECTION_POINT", "VERIFIED"];
    private static readonly string[] Statuses = ["Created", "Picking", "Invoiced", "Shipped", "Delivered", "Cancelled", "UnSupplied"];

    /// <summary>Satıcının API ile yapabildiği geçişler. Shipped/Delivered kargo hareketidir, satıcı set edemez.</summary>
    private static readonly Dictionary<string, string[]> Transitions = new()
    {
        ["Created"] = ["Picking", "Cancelled", "UnSupplied"],
        ["Picking"] = ["Invoiced", "Cancelled", "UnSupplied"],
    };

    private static readonly Dictionary<string, string> CarrierTransitions = new()
    {
        ["Invoiced"] = "Shipped",
        ["Shipped"] = "Delivered",
    };

    private static readonly int[] VatRates = [0, 1, 8, 10, 18, 20];

    public static WebApplication Build(string[] args, SandboxState? state = null)
    {
        state ??= new SandboxState();
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddSingleton(state);
        builder.Services.AddHttpClient();
        var app = builder.Build();

        app.Use(async (ctx, next) =>
        {
            var path = ctx.Request.Path.Value ?? "";
            lock (state.Gate) state.Requests.Add($"{ctx.Request.Method} {path}{ctx.Request.QueryString}");
            if (!path.StartsWith("/integration", StringComparison.OrdinalIgnoreCase)) { await next(); return; }

            if (!TryBasic(ctx.Request.Headers.Authorization.ToString()))
            {
                await Write(ctx, 401, Error("Kimlik doğrulanamadı: Basic apiKey:apiSecret gerekli."));
                return;
            }
            if (string.IsNullOrWhiteSpace(ctx.Request.Headers.UserAgent.ToString()))
            {
                await Write(ctx, 403, Error("User-Agent başlığı '{satıcıId} - {Firma}' biçiminde gönderilmeli."));
                return;
            }
            int failStatus; int? retryAfter;
            lock (state.Gate)
            {
                if (state.FailCount <= 0) { failStatus = 0; retryAfter = null; }
                else { state.FailCount--; failStatus = state.FailStatus; retryAfter = state.FailRetryAfterSeconds; }
            }
            if (failStatus != 0)
            {
                if (retryAfter is not null) ctx.Response.Headers.RetryAfter = retryAfter.Value.ToString();
                await Write(ctx, failStatus, Error("Sandbox: enjekte edilmiş hata."));
                return;
            }
            await next();
        });

        MapCatalog(app);
        MapProducts(app, state);
        MapOrders(app, state);
        MapSandboxAdmin(app, state);
        return app;
    }

    // --- katalog ---

    private static void MapCatalog(WebApplication app)
    {
        app.MapGet("/integration/product/brands", (int? page, int? size) =>
        {
            var all = Enumerable.Range(1, 2500).Select(i => (id: (long)i, name: i == 1 ? "Sandbox Marka" : $"Marka {i}")).ToList();
            var p = page ?? 0; var s = Math.Clamp(size ?? 500, 1, 1000);
            var brands = new JsonArray(all.Skip(p * s).Take(s).Select(b => (JsonNode)new JsonObject { ["id"] = b.id, ["name"] = b.name }).ToArray());
            return Json(new JsonObject { ["brands"] = brands });
        });

        // Belgedeki örnek yanıt sarmalayıcısız bir kategori dizisidir.
        app.MapGet("/integration/product/product-categories", () => Json(
            new JsonArray(
                new JsonObject
                {
                    ["id"] = 1, ["name"] = "Giyim", ["parentId"] = null,
                    ["subCategories"] = new JsonArray(
                        new JsonObject { ["id"] = 11, ["name"] = "Tişört", ["parentId"] = 1, ["subCategories"] = new JsonArray() },
                        new JsonObject { ["id"] = 12, ["name"] = "Pantolon", ["parentId"] = 1, ["subCategories"] = new JsonArray() }),
                },
                new JsonObject { ["id"] = 2, ["name"] = "Elektronik", ["parentId"] = null, ["subCategories"] = new JsonArray(
                    new JsonObject { ["id"] = 21, ["name"] = "Kulaklık", ["parentId"] = 2, ["subCategories"] = new JsonArray() }) })));

        app.MapGet("/integration/product/categories/{categoryId:long}/attributes", (long categoryId) =>
        {
            if (!LeafCategories.Contains(categoryId)) return Json(Error("Kategori bulunamadı veya alt kategorisi var."), 400);
            return Json(new JsonObject
            {
                ["id"] = categoryId,
                ["categoryAttributes"] = new JsonArray(
                    AttrJson(338, "Beden", required: true, allowCustom: false, varianter: true, slicer: false),
                    AttrJson(47, "Renk", required: true, allowCustom: false, varianter: false, slicer: true),
                    AttrJson(910, "Materyal", required: false, allowCustom: true, varianter: false, slicer: false)),
            });
        });

        app.MapGet("/integration/product/categories/{categoryId:long}/attributes/{attributeId:long}/values", (long categoryId, long attributeId, HttpRequest req) =>
        {
            var all = Values(attributeId);
            var size = Math.Clamp(int.TryParse(req.Query["size"], out var sz) ? sz : 10, 1, 1000);
            var page = int.TryParse(req.Query["page"], out var pg) ? Math.Max(pg, 0) : 0;
            var content = all.Skip(page * size).Take(size)
                .Select(v => (JsonNode)new JsonObject { ["attributeValueId"] = v["id"]!.DeepClone(), ["attributeValue"] = v["name"]!.DeepClone() }).ToArray();
            return Json(new JsonObject
            {
                ["totalElements"] = all.Length, ["totalPages"] = (int)Math.Ceiling(all.Length / (double)size), ["page"] = page, ["size"] = size,
                ["content"] = new JsonArray(content),
            });
        });
    }

    private static readonly HashSet<long> LeafCategories = [11, 12, 21];

    private static JsonObject AttrJson(long id, string name, bool required, bool allowCustom, bool varianter, bool slicer) => new()
    {
        ["attribute"] = new JsonObject { ["id"] = id, ["name"] = name },
        ["required"] = required, ["allowCustom"] = allowCustom, ["varianter"] = varianter, ["slicer"] = slicer,
        ["allowMultipleAttributeValues"] = false,
    };

    private static JsonNode[] Values(long attributeId) => attributeId switch
    {
        338 => [new JsonObject { ["id"] = 6980, ["name"] = "S" }, new JsonObject { ["id"] = 6981, ["name"] = "M" }, new JsonObject { ["id"] = 6982, ["name"] = "L" }],
        47 => [new JsonObject { ["id"] = 192, ["name"] = "Siyah" }, new JsonObject { ["id"] = 193, ["name"] = "Beyaz" }],
        _ => [],
    };

    // --- ürün ve stok/fiyat ---

    private static void MapProducts(WebApplication app, SandboxState state)
    {
        app.MapPost("/integration/product/sellers/{sellerId}/v2/products", (string sellerId, JsonNode? body) =>
        {
            var items = body?["items"]?.AsArray();
            if (items is null || items.Count == 0) return Json(Error("items boş olamaz."), 400);
            if (items.Count > 1000) return Json(Error("Bir istekte en fazla 1000 ürün gönderilebilir."), 400);

            lock (state.Gate)
            {
                var results = new List<JsonObject>();
                foreach (var item in items.OfType<JsonObject>())
                {
                    var reasons = ValidateProduct(item);
                    var barcode = item["barcode"]?.ToString() ?? "";
                    if (reasons.Count == 0) state.Products[barcode] = (JsonObject)item.DeepClone();
                    results.Add(BatchItem(item, reasons));
                }
                return Json(new JsonObject { ["batchRequestId"] = NewBatch(state, results) });
            }
        });

        app.MapPost("/integration/inventory/sellers/{sellerId}/products/price-and-inventory", (string sellerId, JsonNode? body) =>
        {
            var items = body?["items"]?.AsArray();
            if (items is null || items.Count == 0) return Json(Error("items boş olamaz."), 400);
            if (items.Count > 1000) return Json(Error("Bir istekte en fazla 1000 ürün gönderilebilir."), 400);

            lock (state.Gate)
            {
                var results = new List<JsonObject>();
                foreach (var item in items.OfType<JsonObject>())
                {
                    var barcode = item["barcode"]?.ToString() ?? "";
                    var reasons = new List<string>();
                    if (!state.Products.TryGetValue(barcode, out var product)) reasons.Add("Barkod bulunamadı.");
                    var qty = item["quantity"]?.GetValue<int>();
                    if (qty is null or < 0 or > 20000) reasons.Add("Stok adedi 0 ile 20000 arasında olmalı.");
                    var sale = item["salePrice"]?.GetValue<decimal>();
                    var list = item["listPrice"]?.GetValue<decimal>();
                    if (sale is null or <= 0) reasons.Add("Satış fiyatı sıfırdan büyük olmalı.");
                    else if (list is not null && sale > list) reasons.Add("Satış fiyatı liste fiyatından büyük olamaz.");

                    if (reasons.Count == 0 && product is not null)
                    {
                        product["quantity"] = qty; product["salePrice"] = sale; product["listPrice"] = list ?? sale;
                    }
                    results.Add(BatchItem(item, reasons));
                }
                return Json(new JsonObject { ["batchRequestId"] = NewBatch(state, results) });
            }
        });

        app.MapGet("/integration/product/sellers/{sellerId}/products/batch-requests/{batchId}", (string sellerId, string batchId) =>
        {
            lock (state.Gate)
            {
                if (!state.Batches.TryGetValue(batchId, out var batch)) return Json(Error("Batch bulunamadı."), 404);
                if (batch.PendingPolls > 0)
                {
                    batch.PendingPolls--;
                    return Json(new JsonObject { ["batchRequestId"] = batchId, ["status"] = "IN_PROGRESS", ["itemCount"] = batch.Items.Count, ["items"] = new JsonArray() });
                }
                var failed = batch.Items.Count(i => i["status"]!.ToString() == "FAILED");
                return Json(new JsonObject
                {
                    ["batchRequestId"] = batchId, ["status"] = "COMPLETED", ["itemCount"] = batch.Items.Count, ["failedItemCount"] = failed,
                    ["items"] = new JsonArray(batch.Items.Select(i => (JsonNode)i.DeepClone()).ToArray()),
                });
            }
        });
    }

    private static List<string> ValidateProduct(JsonObject p)
    {
        var r = new List<string>();
        if (string.IsNullOrWhiteSpace(p["barcode"]?.ToString())) r.Add("Barkod zorunlu.");
        var title = p["title"]?.ToString() ?? "";
        if (title.Length == 0) r.Add("Ürün adı zorunlu.");
        else if (title.Length > 100) r.Add("Ürün adı en fazla 100 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(p["productMainId"]?.ToString())) r.Add("productMainId zorunlu.");
        if ((p["description"]?.ToString() ?? "").Length is 0 or > 30000) r.Add("Açıklama 1 ile 30000 karakter arasında olmalı.");
        if (p["brandId"]?.GetValue<long>() is null or <= 0) r.Add("Marka bulunamadı.");
        var cat = p["categoryId"]?.GetValue<long>();
        if (cat is null || !LeafCategories.Contains(cat.Value)) r.Add("Kategori bulunamadı veya son kategori değil.");
        if (p["images"] is not JsonArray { Count: > 0 }) r.Add("En az bir görsel zorunlu.");
        var qty = p["quantity"]?.GetValue<int>();
        if (qty is null or < 0 or > 20000) r.Add("Stok adedi 0 ile 20000 arasında olmalı.");
        var sale = p["salePrice"]?.GetValue<decimal>(); var list = p["listPrice"]?.GetValue<decimal>();
        if (sale is null or <= 0) r.Add("Satış fiyatı sıfırdan büyük olmalı.");
        else if (list is not null && sale > list) r.Add("Satış fiyatı liste fiyatından büyük olamaz.");
        var vat = p["vatRate"]?.GetValue<int>();
        if (vat is null || !VatRates.Contains(vat.Value)) r.Add("KDV oranı geçersiz.");

        if (cat is not null && LeafCategories.Contains(cat.Value))
        {
            var attrs = (p["attributes"] as JsonArray ?? []).OfType<JsonObject>().ToList();
            foreach (var a in attrs)
                if ((a["attributeValueId"] is null) == (a["customAttributeValue"] is null))
                    r.Add($"Özellik {a["attributeId"]}: attributeValueId ya da customAttributeValue'dan yalnızca biri gönderilmeli.");
            var sent = attrs.Select(a => a["attributeId"]?.GetValue<long>()).ToHashSet();
            foreach (var (id, name) in new[] { (338L, "Beden"), (47L, "Renk") })
                if (!sent.Contains(id)) r.Add($"Zorunlu özellik eksik: {name} ({id}).");
        }
        return r;
    }

    private static JsonObject BatchItem(JsonObject item, List<string> reasons) => new()
    {
        ["requestItem"] = new JsonObject { ["product"] = item.DeepClone(), ["barcode"] = item["barcode"]?.DeepClone() },
        ["status"] = reasons.Count == 0 ? "SUCCESS" : "FAILED",
        ["failureReasons"] = new JsonArray(reasons.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray()),
    };

    private static string NewBatch(SandboxState state, List<JsonObject> items)
    {
        var id = state.NewId();
        state.Batches[id] = new SandboxBatch(id, items, state.PendingPolls);
        return id;
    }

    // --- siparişler ve webhook kaydı ---

    private static void MapOrders(WebApplication app, SandboxState state)
    {
        app.MapGet("/integration/order/sellers/{sellerId}/v2/orders", (string sellerId, HttpRequest req) =>
        {
            var q = req.Query;
            var page = int.TryParse(q["page"], out var pg) ? Math.Max(pg, 0) : 0;
            var size = Math.Clamp(int.TryParse(q["size"], out var sz) ? sz : 50, 1, 200);
            var status = q["status"].ToString();
            if (status.Length > 0 && !Statuses.Contains(status)) return Json(Error($"Geçersiz status: {status}"), 400);
            if (size != (int.TryParse(q["size"], out var asked) ? asked : 50)) return Json(Error("size en çok 200 olabilir."), 400);
            if (long.TryParse(q["startDate"], out var s0) && long.TryParse(q["endDate"], out var e0) && e0 - s0 > TimeSpan.FromDays(14).TotalMilliseconds)
                return Json(Error("startDate ile endDate arası en çok 2 hafta olabilir."), 400);
            var byModified = q["orderByField"] == "PackageLastModifiedDate";

            lock (state.Gate)
            {
                IEnumerable<SandboxOrder> orders = state.Orders;
                if (status.Length > 0) orders = orders.Where(o => o.Status == status);
                // PackageLastModifiedDate ile sıralanınca tarih süzgeci paketin son değişim zamanına uygulanır.
                DateTimeOffset Stamp(SandboxOrder o) => byModified ? o.LastModified : o.OrderDate;
                if (long.TryParse(q["startDate"], out var start)) orders = orders.Where(o => Stamp(o).ToUnixTimeMilliseconds() >= start);
                if (long.TryParse(q["endDate"], out var end)) orders = orders.Where(o => Stamp(o).ToUnixTimeMilliseconds() <= end);
                var list = orders.OrderByDescending(o => o.LastModified).ToList();
                var totalPages = (int)Math.Ceiling(list.Count / (double)size);
                return Json(new JsonObject
                {
                    ["page"] = page, ["size"] = size, ["totalElements"] = list.Count, ["totalPages"] = totalPages,
                    ["content"] = new JsonArray(list.Skip(page * size).Take(size).Select(o => (JsonNode)OrderJson(o)).ToArray()),
                });
            }
        });

        app.MapPut("/integration/order/sellers/{sellerId}/shipment-packages/{packageId:long}", (string sellerId, long packageId, JsonNode? body) =>
        {
            var next = body?["status"]?.ToString() ?? "";
            lock (state.Gate)
            {
                var order = state.Orders.FirstOrDefault(o => o.PackageId == packageId);
                if (order is null) return Json(Error("Paket bulunamadı."), 404);
                if (!Transitions.TryGetValue(order.Status, out var allowed) || !allowed.Contains(next))
                    return Json(Error($"{order.Status} durumundaki paket {next} durumuna geçirilemez."), 400);
                if (next == "Invoiced" && string.IsNullOrWhiteSpace(body?["params"]?["invoiceNumber"]?.ToString()))
                    return Json(Error("Invoiced için params.invoiceNumber zorunlu."), 400);
                var lineIds = (body?["lines"] as JsonArray ?? []).OfType<JsonObject>().Select(l => l["lineId"]?.GetValue<long>()).ToList();
                if (lineIds.Count == 0 || lineIds.Any(id => order.Lines.All(l => l.Id != id)))
                    return Json(Error("lines paketin satırlarıyla eşleşmiyor."), 400);

                order.Status = next; order.LastModified = DateTimeOffset.UtcNow;
                return Json(new JsonObject());
            }
        });

        app.MapPost("/integration/webhook/sellers/{sellerId}/webhooks", (string sellerId, JsonNode? body) =>
        {
            var url = body?["url"]?.ToString() ?? "";
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                return Json(Error("Webhook adresi https olmalı."), 400);
            if (body?["authenticationType"]?.ToString() != "BASIC_AUTHENTICATION"
                || string.IsNullOrWhiteSpace(body["username"]?.ToString()) || string.IsNullOrWhiteSpace(body["password"]?.ToString()))
                return Json(Error("BASIC_AUTHENTICATION için username ve password zorunlu."), 400);
            var bad = (body?["subscribedStatuses"] as JsonArray ?? []).Select(x => x?.ToString() ?? "").FirstOrDefault(x => !WebhookStatuses.Contains(x));
            if (bad is not null) return Json(Error($"Geçersiz subscribedStatuses değeri: {bad}"), 400);
            lock (state.Gate)
            {
                if (state.Webhooks.Count >= 15) return Json(Error("En fazla 15 webhook tanımlanabilir."), 400);
                var hook = (JsonObject)body.DeepClone();
                hook["id"] = state.NewId();
                state.Webhooks.Add(hook);
                return Json(new JsonObject { ["id"] = hook["id"]!.ToString() });
            }
        });
    }

    internal static JsonObject OrderJson(SandboxOrder o) => new()
    {
        ["orderNumber"] = o.OrderNumber, ["shipmentPackageId"] = o.PackageId,
        ["status"] = o.Status, ["orderDate"] = o.OrderDate.ToUnixTimeMilliseconds(),
        ["lastModifiedDate"] = o.LastModified.ToUnixTimeMilliseconds(),
        ["packageTotalPrice"] = o.Total, ["packageGrossAmount"] = o.Total,
        ["customerFirstName"] = o.CustomerFirstName, ["customerLastName"] = o.CustomerLastName,
        ["lines"] = new JsonArray(o.Lines.Select(l => (JsonNode)new JsonObject
        {
            ["lineId"] = l.Id, ["barcode"] = l.Barcode, ["productName"] = l.Name, ["quantity"] = l.Quantity, ["lineGrossAmount"] = l.Amount,
        }).ToArray()),
    };

    // --- sandbox yönetim uçları ---

    private static void MapSandboxAdmin(WebApplication app, SandboxState state)
    {
        app.MapPost("/__sandbox/reset", () => { lock (state.Gate) state.Reset(); return Json(new JsonObject { ["ok"] = true }); });

        // {"count":2,"status":429,"retryAfterSeconds":1}: sıradaki 2 /integration isteği 429 döner.
        app.MapPost("/__sandbox/fail", (JsonNode? body) =>
        {
            lock (state.Gate)
            {
                state.FailCount = body?["count"]?.GetValue<int>() ?? 1;
                state.FailStatus = body?["status"]?.GetValue<int>() ?? 500;
                state.FailRetryAfterSeconds = body?["retryAfterSeconds"]?.GetValue<int>();
            }
            return Json(new JsonObject { ["ok"] = true });
        });

        app.MapPost("/__sandbox/pending-polls", (JsonNode? body) =>
        {
            lock (state.Gate) state.PendingPolls = body?["count"]?.GetValue<int>() ?? 0;
            return Json(new JsonObject { ["ok"] = true });
        });

        // {"count":3,"barcode":"BC-1","status":"Created","price":100}
        app.MapPost("/__sandbox/orders", (JsonNode? body) =>
        {
            var count = Math.Clamp(body?["count"]?.GetValue<int>() ?? 1, 1, 500);
            var barcode = body?["barcode"]?.ToString() ?? "SBX-BARCODE-1";
            var price = body?["price"]?.GetValue<decimal>() ?? 100m;
            var status = body?["status"]?.ToString() ?? "Created";
            var created = new JsonArray();
            lock (state.Gate)
            {
                for (var i = 0; i < count; i++)
                {
                    var n = state.Orders.Count + 1;
                    var line = new SandboxLine(state.NextLineId++, barcode, "Sandbox ürünü", 1 + i % 3, price);
                    var order = new SandboxOrder($"SBX{10_000_000 + n}", 7_000_000 + n, DateTimeOffset.UtcNow.AddMinutes(-n), "Test", $"Müşteri{n}", [line]) { Status = status };
                    state.Orders.Add(order);
                    created.Add(OrderJson(order));
                }
            }
            return Json(new JsonObject { ["created"] = created });
        });

        // Kargo firmasının paketi ilerletmesi: Invoiced → Shipped → Delivered ({"status":"Shipped"}).
        app.MapPost("/__sandbox/orders/{packageId:long}/carrier", (long packageId, JsonNode? body) =>
        {
            var next = body?["status"]?.ToString() ?? "";
            lock (state.Gate)
            {
                var order = state.Orders.FirstOrDefault(o => o.PackageId == packageId);
                if (order is null) return Json(Error("Paket bulunamadı."), 404);
                if (!CarrierTransitions.TryGetValue(order.Status, out var allowed) || allowed != next)
                    return Json(Error($"{order.Status} durumundaki paket kargoda {next} olamaz."), 400);
                order.Status = next; order.LastModified = DateTimeOffset.UtcNow;
                return Json(OrderJson(order));
            }
        });

        // Gerçek Trendyol'un yaptığı gibi, kayıtlı alıcıya (ya da verilen adrese) Basic kimlikle paket JSON'u gönderir.
        // {"packageId":7000001,"url":"http://localhost:5000/api/webhooks/trendyol/...","username":"u","password":"p"}
        app.MapPost("/__sandbox/webhooks/send", async (JsonNode? body, IHttpClientFactory http) =>
        {
            SandboxOrder? order; string url = body?["url"]?.ToString() ?? "";
            string user = body?["username"]?.ToString() ?? "", pass = body?["password"]?.ToString() ?? "";
            lock (state.Gate)
            {
                var id = body?["packageId"]?.GetValue<long>();
                order = id is null ? state.Orders.LastOrDefault() : state.Orders.FirstOrDefault(o => o.PackageId == id);
                if (url.Length == 0 && state.Webhooks.FirstOrDefault() is { } h) { url = h["url"]!.ToString(); user = h["username"]!.ToString(); pass = h["password"]!.ToString(); }
            }
            if (order is null) return Json(Error("Gönderilecek sipariş yok."), 404);
            if (url.Length == 0) return Json(Error("Adres yok: url verin ya da önce webhook kaydedin."), 400);

            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(OrderJson(order).ToJsonString(), Encoding.UTF8, "application/json"),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}")));
            using var resp = await http.CreateClient().SendAsync(req);
            return Json(new JsonObject { ["deliveredTo"] = url, ["status"] = (int)resp.StatusCode });
        });

        app.MapGet("/__sandbox/state", () =>
        {
            lock (state.Gate)
                return Json(new JsonObject
                {
                    ["products"] = new JsonArray(state.Products.Values.Select(p => (JsonNode)p.DeepClone()).ToArray()),
                    ["orders"] = new JsonArray(state.Orders.Select(o => (JsonNode)OrderJson(o)).ToArray()),
                    ["webhooks"] = new JsonArray(state.Webhooks.Select(w => (JsonNode)w.DeepClone()).ToArray()),
                    ["requests"] = new JsonArray(state.Requests.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray()),
                });
        });
    }

    // --- yardımcılar ---

    private static bool TryBasic(string header)
    {
        if (!AuthenticationHeaderValue.TryParse(header, out var h) || h.Scheme != "Basic" || h.Parameter is null) return false;
        try
        {
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(h.Parameter));
            var i = text.IndexOf(':');
            return i > 0 && i < text.Length - 1;
        }
        catch (FormatException) { return false; }
    }

    private static JsonObject Error(string message) => new() { ["errors"] = new JsonArray(new JsonObject { ["message"] = message }) };

    private static IResult Json(JsonNode node, int status = 200) => Results.Content(node.ToJsonString(), "application/json", Encoding.UTF8, status);

    private static Task Write(HttpContext ctx, int status, JsonNode node)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        return ctx.Response.WriteAsync(node.ToJsonString());
    }
}
