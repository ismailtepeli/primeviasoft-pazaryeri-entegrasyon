using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pazaryeri.Api.Modules;
using Pazaryeri.Application.Dealers;
using Pazaryeri.Application.Modules;
using Pazaryeri.Domain;
using Pazaryeri.Infrastructure.Persistence;

namespace Pazaryeri.Api.Controllers;

public record DealerRowDto(Guid Id, string Name, string TaxNumber, string? ContactName, string Email, string? Phone, string? City,
    string Status, Guid? GroupId, string? GroupName, DateTime CreatedAt);

/// <summary>Durum sekmeleri sayaçları; aramadan bağımsız.</summary>
public record DealerCounts(int Pending, int Active, int Passive);

public record DealerListPage(IReadOnlyList<DealerRowDto> Items, int Total, DealerCounts Counts);

public record DealerUserDto(Guid Id, string Email, string FullName, string Role, bool IsActive, DateTime? LastLoginAt);

public record DealerDetailDto(Guid Id, string Name, string TaxNumber, string? TaxOffice, string? ContactName, string Email, string? Phone,
    string? Address, string? City, string? Notes, string Status, Guid? GroupId, DateTime CreatedAt, DateTime? ApprovedAt, IReadOnlyList<DealerUserDto> Users);

public record DealerInput(string? Name, string? TaxNumber, string? TaxOffice, string? ContactName, string? Email, string? Phone,
    string? Address, string? City, string? Notes, Guid? GroupId);

public record DealerUserInput(string? Email, string? FullName, string? Password, string? Role);

public record DealerUserUpdate(string? FullName, bool? IsActive, string? NewPassword);

public record DealerGroupDto(Guid Id, string Name, string? Description, int DealerCount);

public record DealerGroupInput(string? Name, string? Description);

[ApiController]
[Route("api/dealers")]
[RequiresModule(ModuleIds.Dealers)]
public class DealersController(AppDbContext db) : ControllerBase
{
    public const int MaxPageSize = 100;
    private readonly PasswordHasher<DealerUser> _hasher = new();

    /// <summary>Bayi listesi: durum sekmesi, arama (ad, vergi no, e-posta, yetkili), sunucu taraflı sayfalama; en yeni önce.</summary>
    [HttpGet]
    public async Task<ActionResult<DealerListPage>> List([FromQuery] string? status, [FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        page = Math.Max(page, 1);
        DealerStatus? filter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<DealerStatus>(status, true, out var parsed)) return BadRequest(new { error = "Bilinmeyen durum." });
            filter = parsed;
        }

        var by = await db.Dealers.AsNoTracking().GroupBy(d => d.Status).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var counts = new DealerCounts(by.GetValueOrDefault(DealerStatus.Pending), by.GetValueOrDefault(DealerStatus.Active), by.GetValueOrDefault(DealerStatus.Passive));

        var query = db.Dealers.AsNoTracking();
        if (filter is { } f) query = query.Where(d => d.Status == f);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(d => d.Name.Contains(term) || d.TaxNumber.Contains(term) || d.Email.Contains(term) || (d.ContactName != null && d.ContactName.Contains(term)));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(d => new
            {
                d.Id, d.Name, d.TaxNumber, d.ContactName, d.Email, d.Phone, d.City, d.Status, d.GroupId, d.CreatedAt,
                GroupName = db.DealerGroups.Where(g => g.Id == d.GroupId).Select(g => g.Name).FirstOrDefault(),
            }).ToListAsync(ct);
        var items = rows.Select(d => new DealerRowDto(d.Id, d.Name, d.TaxNumber, d.ContactName, d.Email, d.Phone, d.City, d.Status.ToString(), d.GroupId, d.GroupName, d.CreatedAt)).ToList();
        return new DealerListPage(items, total, counts);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DealerDetailDto>> Get(Guid id, CancellationToken ct)
    {
        var dealer = await db.Dealers.AsNoTracking().Include(d => d.Users).FirstOrDefaultAsync(d => d.Id == id, ct);
        return dealer is null ? NotFound(new { error = "Bayi bulunamadı." }) : Detail(dealer);
    }

    /// <summary>Yönetici bayiyi doğrudan Aktif olarak ekler (başvuru beklemez).</summary>
    [HttpPost]
    public async Task<ActionResult<DealerDetailDto>> Create(DealerInput input, CancellationToken ct)
    {
        var error = await ValidateAsync(input, null, ct);
        if (error is not null) return error;
        var dealer = new Dealer { Status = DealerStatus.Active, ApprovedAt = DateTime.UtcNow };
        Apply(dealer, input);
        db.Dealers.Add(dealer);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = dealer.Id }, Detail(dealer));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<DealerDetailDto>> Update(Guid id, DealerInput input, CancellationToken ct)
    {
        var dealer = await db.Dealers.Include(d => d.Users).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (dealer is null) return NotFound(new { error = "Bayi bulunamadı." });
        var error = await ValidateAsync(input, id, ct);
        if (error is not null) return error;
        Apply(dealer, input);
        await db.SaveChangesAsync(ct);
        return Detail(dealer);
    }

    /// <summary>Başvuruyu onaylar (Beklemede → Aktif).</summary>
    [HttpPost("{id:guid}/approve")]
    public Task<ActionResult<DealerDetailDto>> Approve(Guid id, CancellationToken ct) => Transition(id, DealerStatus.Active, [DealerStatus.Pending], "Yalnızca bekleyen başvuru onaylanır.", ct);

    /// <summary>Bayiyi pasife alır; bekleyen başvurudaysa reddeder. Aktif ya da bekleyen bayi pasife alınabilir.</summary>
    [HttpPost("{id:guid}/deactivate")]
    public Task<ActionResult<DealerDetailDto>> Deactivate(Guid id, CancellationToken ct) => Transition(id, DealerStatus.Passive, [DealerStatus.Active, DealerStatus.Pending], "Bayi zaten pasif.", ct);

    /// <summary>Pasif bayiyi yeniden aktif yapar.</summary>
    [HttpPost("{id:guid}/activate")]
    public Task<ActionResult<DealerDetailDto>> Activate(Guid id, CancellationToken ct) => Transition(id, DealerStatus.Active, [DealerStatus.Passive], "Yalnızca pasif bayi yeniden aktif edilir.", ct);

    private async Task<ActionResult<DealerDetailDto>> Transition(Guid id, DealerStatus to, DealerStatus[] allowedFrom, string deniedMessage, CancellationToken ct)
    {
        var dealer = await db.Dealers.Include(d => d.Users).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (dealer is null) return NotFound(new { error = "Bayi bulunamadı." });
        if (!allowedFrom.Contains(dealer.Status)) return Conflict(new { error = deniedMessage });
        dealer.Status = to;
        if (to == DealerStatus.Active) dealer.ApprovedAt ??= DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Detail(dealer);
    }

    // --- kullanıcılar ---

    [HttpPost("{id:guid}/users")]
    public async Task<ActionResult<DealerUserDto>> AddUser(Guid id, DealerUserInput input, CancellationToken ct)
    {
        if (!await db.Dealers.AnyAsync(d => d.Id == id, ct)) return NotFound(new { error = "Bayi bulunamadı." });
        var error = DealerRules.ValidateEmail(input.Email) ?? DealerRules.ValidatePassword(input.Password)
            ?? (string.IsNullOrWhiteSpace(input.FullName) ? "Ad soyad zorunlu." : DealerRules.ValidateLength(input.FullName, 150, "Ad soyad"));
        if (error is not null) return BadRequest(new { error });
        var role = DealerUserRole.Buyer;
        if (!string.IsNullOrWhiteSpace(input.Role) && !Enum.TryParse(input.Role, true, out role)) return BadRequest(new { error = "Bilinmeyen rol." });

        var email = DealerRules.NormalizeEmail(input.Email!);
        if (await db.DealerUsers.AnyAsync(u => u.Email == email, ct)) return Conflict(new { error = "Bu e-posta başka bir bayi kullanıcısında kayıtlı." });
        var user = new DealerUser { DealerId = id, Email = email, FullName = input.FullName!.Trim(), Role = role };
        user.PasswordHash = _hasher.HashPassword(user, input.Password!);
        db.DealerUsers.Add(user);
        await db.SaveChangesAsync(ct);
        return Created($"api/dealers/{id}", UserDto(user));
    }

    [HttpPut("{id:guid}/users/{userId:guid}")]
    public async Task<ActionResult<DealerUserDto>> UpdateUser(Guid id, Guid userId, DealerUserUpdate input, CancellationToken ct)
    {
        var user = await db.DealerUsers.FirstOrDefaultAsync(u => u.Id == userId && u.DealerId == id, ct);
        if (user is null) return NotFound(new { error = "Kullanıcı bulunamadı." });
        if (input.FullName is not null)
        {
            var nameError = string.IsNullOrWhiteSpace(input.FullName) ? "Ad soyad zorunlu." : DealerRules.ValidateLength(input.FullName, 150, "Ad soyad");
            if (nameError is not null) return BadRequest(new { error = nameError });
            user.FullName = input.FullName.Trim();
        }
        if (input.NewPassword is not null)
        {
            var pwError = DealerRules.ValidatePassword(input.NewPassword);
            if (pwError is not null) return BadRequest(new { error = pwError });
            user.PasswordHash = _hasher.HashPassword(user, input.NewPassword);
        }
        if (input.IsActive is { } active) user.IsActive = active;
        await db.SaveChangesAsync(ct);
        return UserDto(user);
    }

    // --- yardımcılar ---

    private async Task<ActionResult?> ValidateAsync(DealerInput i, Guid? existingId, CancellationToken ct)
    {
        var error = string.IsNullOrWhiteSpace(i.Name) ? "Firma adı zorunlu." : DealerRules.ValidateLength(i.Name, 200, "Firma adı")
            ?? DealerRules.ValidateTaxNumber(i.TaxNumber) ?? DealerRules.ValidateEmail(i.Email)
            ?? DealerRules.ValidateLength(i.TaxOffice, 100, "Vergi dairesi") ?? DealerRules.ValidateLength(i.ContactName, 150, "Yetkili adı")
            ?? DealerRules.ValidateLength(i.Phone, 40, "Telefon") ?? DealerRules.ValidateLength(i.City, 100, "Şehir")
            ?? DealerRules.ValidateLength(i.Address, 500, "Adres") ?? DealerRules.ValidateLength(i.Notes, 2000, "Not");
        if (error is not null) return BadRequest(new { error });
        var tax = i.TaxNumber!.Trim();
        if (await db.Dealers.AnyAsync(d => d.TaxNumber == tax && d.Id != existingId, ct)) return Conflict(new { error = "Bu vergi numarasıyla başka bir bayi kayıtlı." });
        if (i.GroupId is { } g && !await db.DealerGroups.AnyAsync(x => x.Id == g, ct)) return BadRequest(new { error = "Bayi grubu bulunamadı." });
        return null;
    }

    private static void Apply(Dealer d, DealerInput i)
    {
        d.Name = i.Name!.Trim();
        d.TaxNumber = i.TaxNumber!.Trim();
        d.TaxOffice = DealerRules.Clean(i.TaxOffice);
        d.ContactName = DealerRules.Clean(i.ContactName);
        d.Email = DealerRules.NormalizeEmail(i.Email!);
        d.Phone = DealerRules.Clean(i.Phone);
        d.Address = DealerRules.Clean(i.Address);
        d.City = DealerRules.Clean(i.City);
        d.Notes = DealerRules.Clean(i.Notes);
        d.GroupId = i.GroupId;
    }

    private static DealerUserDto UserDto(DealerUser u) => new(u.Id, u.Email, u.FullName, u.Role.ToString(), u.IsActive, u.LastLoginAt);

    private static DealerDetailDto Detail(Dealer d) => new(d.Id, d.Name, d.TaxNumber, d.TaxOffice, d.ContactName, d.Email, d.Phone, d.Address, d.City, d.Notes,
        d.Status.ToString(), d.GroupId, d.CreatedAt, d.ApprovedAt, d.Users.OrderBy(u => u.CreatedAt).Select(UserDto).ToList());
}

[ApiController]
[Route("api/dealer-groups")]
[RequiresModule(ModuleIds.Dealers)]
public class DealerGroupsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<List<DealerGroupDto>> List(CancellationToken ct) =>
        await db.DealerGroups.AsNoTracking().OrderBy(g => g.Name)
            .Select(g => new DealerGroupDto(g.Id, g.Name, g.Description, db.Dealers.Count(d => d.GroupId == g.Id))).ToListAsync(ct);

    [HttpPost]
    public async Task<ActionResult<DealerGroupDto>> Create(DealerGroupInput input, CancellationToken ct)
    {
        var error = Validate(input);
        if (error is not null) return BadRequest(new { error });
        var name = input.Name!.Trim();
        if (await db.DealerGroups.AnyAsync(g => g.Name == name, ct)) return Conflict(new { error = "Bu adla bir grup zaten var." });
        var group = new DealerGroup { Name = name, Description = DealerRules.Clean(input.Description) };
        db.DealerGroups.Add(group);
        await db.SaveChangesAsync(ct);
        return Created($"api/dealer-groups/{group.Id}", new DealerGroupDto(group.Id, group.Name, group.Description, 0));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<DealerGroupDto>> Update(Guid id, DealerGroupInput input, CancellationToken ct)
    {
        var group = await db.DealerGroups.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group is null) return NotFound(new { error = "Grup bulunamadı." });
        var error = Validate(input);
        if (error is not null) return BadRequest(new { error });
        var name = input.Name!.Trim();
        if (await db.DealerGroups.AnyAsync(g => g.Name == name && g.Id != id, ct)) return Conflict(new { error = "Bu adla bir grup zaten var." });
        group.Name = name;
        group.Description = DealerRules.Clean(input.Description);
        await db.SaveChangesAsync(ct);
        return new DealerGroupDto(group.Id, group.Name, group.Description, await db.Dealers.CountAsync(d => d.GroupId == id, ct));
    }

    /// <summary>Grupta bayi varsa silinmez (önce bayilerin grubu değiştirilmeli).</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var group = await db.DealerGroups.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group is null) return NotFound(new { error = "Grup bulunamadı." });
        var count = await db.Dealers.CountAsync(d => d.GroupId == id, ct);
        if (count > 0) return Conflict(new { error = $"Bu grupta {count} bayi var; önce bayilerin grubunu değiştirin." });
        db.DealerGroups.Remove(group);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static string? Validate(DealerGroupInput i) =>
        string.IsNullOrWhiteSpace(i.Name) ? "Grup adı zorunlu." : DealerRules.ValidateLength(i.Name, 100, "Grup adı") ?? DealerRules.ValidateLength(i.Description, 500, "Açıklama");
}
