using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>
/// Approved RouterOS User Manager payments are reported only in a SINGLE
/// known currency. Exchange rates are never guessed or silently applied.
/// </summary>
public static class UserManagerRevenueMath
{
    public static bool TryTotalSingleCurrency(
        IReadOnlyList<UmApprovedPayment> payments,out decimal amount,out string currency)
    {
        ArgumentNullException.ThrowIfNull(payments);
        amount=0;
        currency="";
        if(payments.Count==0)return true;
        var first=payments[0].Currency?.Trim();
        if(string.IsNullOrWhiteSpace(first) ||
            first.Equals("غير متاح",StringComparison.OrdinalIgnoreCase))
            return false;
        decimal sum=0;
        foreach(var payment in payments)
        {
            if(payment.Amount<0 ||
                !string.Equals(payment.Currency?.Trim(),first,
                    StringComparison.OrdinalIgnoreCase))
                return false;
            sum=checked(sum+payment.Amount);
        }
        amount=sum;
        currency=first;
        return true;
    }
}
