namespace Pazaryeri.Application.Dealers;

public record AccountOrder(DateTime Date, decimal Total);

/// <param name="Balance">Bayinin borcu (siparişler − ödemeler); negatifse bayi alacaklıdır.</param>
/// <param name="Overdue">Vadesi geçmiş ödenmemiş tutar.</param>
/// <param name="OldestUnpaidDate">Hâlâ ödenmemiş en eski siparişin tarihi; borç yoksa null.</param>
public record AccountSummary(decimal TotalOrders, decimal TotalPayments, decimal Balance, decimal Overdue, DateTime? OldestUnpaidDate);

/// <summary>
/// Basit bayi cari hesabı. Ödemeler en eski siparişten başlayarak (FIFO) kapatılır; bir siparişin ödenmemiş kısmı,
/// sipariş tarihi + vade günü geçmişse vadesi geçmiş sayılır. İptal/iade siparişler hesaba girmez (çağıran süzer).
/// </summary>
public static class DealerAccount
{
    public static AccountSummary Compute(IEnumerable<AccountOrder> orders, IEnumerable<decimal> payments, int termDays, DateTime now)
    {
        var list = orders.OrderBy(o => o.Date).ToList();
        var totalOrders = list.Sum(o => o.Total);
        var totalPayments = payments.Sum();
        var remaining = totalPayments;
        decimal overdue = 0;
        DateTime? oldest = null;
        foreach (var o in list)
        {
            var paid = Math.Min(Math.Max(remaining, 0), o.Total);
            remaining -= paid;
            var unpaid = o.Total - paid;
            if (unpaid <= 0) continue;
            oldest ??= o.Date;
            if (o.Date.AddDays(Math.Max(termDays, 0)) < now) overdue += unpaid;
        }
        return new AccountSummary(totalOrders, totalPayments, totalOrders - totalPayments, overdue, oldest);
    }

    public static string? ValidatePayment(decimal amount, string? note)
    {
        if (amount <= 0) return "Ödeme tutarı sıfırdan büyük olmalı.";
        if (amount > 100_000_000m) return "Ödeme tutarı 100.000.000'u aşamaz.";
        if (note is not null && note.Trim().Length > 300) return "Not en fazla 300 karakter olabilir.";
        return null;
    }

    public static string? ValidateTerm(int termDays) => termDays is < 0 or > 365 ? "Vade 0 ile 365 gün arasında olmalı." : null;
}
