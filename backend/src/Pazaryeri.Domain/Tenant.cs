using Pazaryeri.Domain.Common;

namespace Pazaryeri.Domain;

/// <summary>Kiracı (müşteri firma). Kiracı filtresinin dışındadır.</summary>
public class Tenant : Entity
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
