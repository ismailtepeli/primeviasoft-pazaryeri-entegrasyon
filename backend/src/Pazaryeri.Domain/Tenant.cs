using Pazaryeri.Domain.Common;

namespace Pazaryeri.Domain;

/// <summary>Kiracı (müşteri firma). Kiracı filtresinin dışındadır.</summary>
public class Tenant : Entity
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    /// <summary>Bayi sitesinde kiracıyı bulmak için kısa ad (küçük harf, rakam, tire); alt alan adı çözümlemesi (#68) bunu kullanacak. Tekil, boş olabilir.</summary>
    public string? Slug { get; set; }
}
