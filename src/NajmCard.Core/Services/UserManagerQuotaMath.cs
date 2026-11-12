using System.Globalization;

namespace NajmCard.Core.Services;

/// <summary>
/// Lossless RouterOS transfer-limit editing in GiB. An existing quota may
/// contain an arbitrary integer byte count, not a whole or 0.001 GiB value.
/// Rounding the displayed GiB to 3 decimals silently changes that limit.
/// </summary>
public static class UserManagerQuotaMath
{
    private const decimal BytesPerGiB=1073741824m;

    public static string ToEditorGiB(long bytes)
    {
        if(bytes<0)throw new ArgumentOutOfRangeException(nameof(bytes));
        // Ten fractional digits resolve a single byte at GiB scale. The
        // formatter limits no meaningful precision for a long byte count.
        return (bytes/BytesPerGiB).ToString("0.##########",
            CultureInfo.InvariantCulture);
    }

    public static string ParseGiBToByteString(string? text)
    {
        if(string.IsNullOrWhiteSpace(text))return "";
        if(!decimal.TryParse(text.Trim(),NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,out var gib) || gib<0)
            throw new ArgumentException("حجم الباقة بالـ GiB غير صالح.",nameof(text));
        // The ten-digit editor display can round a true max-long GiB value
        // slightly ABOVE its mathematically exact quotient. Validate the
        // nearest whole byte, not the unrounded decimal display.
        if(gib>long.MaxValue/BytesPerGiB+1m)
            throw new ArgumentException("حجم الباقة يتجاوز حدود RouterOS.",nameof(text));
        decimal bytes;
        try {bytes=decimal.Round(gib*BytesPerGiB,0,MidpointRounding.AwayFromZero);}
        catch(OverflowException)
        {
            throw new ArgumentException("حجم الباقة يتجاوز حدود RouterOS.",nameof(text));
        }
        if(bytes>long.MaxValue)
            throw new ArgumentException("حجم الباقة يتجاوز حدود RouterOS.",nameof(text));
        return checked((long)bytes).ToString(CultureInfo.InvariantCulture);
    }
}
