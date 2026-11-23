namespace NajmCard.Desktop.Services;

/// <summary>Windows supplies the work area in device-independent units.</summary>
public static class WindowViewport
{
    public static (double Width,double Height) Fit(double availableWidth,double availableHeight)
    {
        if(!double.IsFinite(availableWidth)||availableWidth<=0)
            throw new ArgumentOutOfRangeException(nameof(availableWidth));
        if(!double.IsFinite(availableHeight)||availableHeight<=0)
            throw new ArgumentOutOfRangeException(nameof(availableHeight));
        return (Math.Min(1260,availableWidth),Math.Min(730,availableHeight));
    }
}
