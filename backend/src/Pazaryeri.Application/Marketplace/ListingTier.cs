namespace Pazaryeri.Application.Marketplace;

/// <summary>Satıcının kanaldaki ürün listeleme seviyesi; API limitleri buna göre artar.</summary>
public enum ListingTier
{
    Up50k,
    Up75k,
    Up150k,
    Up500k,
    Unlimited,
}
