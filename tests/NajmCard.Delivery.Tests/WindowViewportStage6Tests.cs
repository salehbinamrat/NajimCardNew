using NajmCard.Desktop.Services;
using Xunit;

namespace NajmCard.Delivery.Tests;

public sealed class WindowViewportStage6Tests
{
    [Theory]
    [InlineData(1920,1040,1.0)]
    [InlineData(1920,1040,1.25)]
    [InlineData(1920,1040,1.5)]
    [InlineData(1366,728,1.5)]
    public void FixedDesignFitsTheAvailableWindowsWorkArea(int pixelsX,int pixelsY,double dpi)
    {
        var fit=WindowViewport.Fit(pixelsX/dpi,pixelsY/dpi);
        Assert.InRange(fit.Width,1,1260);
        Assert.InRange(fit.Height,1,730);
        Assert.True(fit.Width*dpi<=pixelsX+0.01);
        Assert.True(fit.Height*dpi<=pixelsY+0.01);
    }

    [Fact]
    public void ApprovedSizeIsUnchangedWhenTheWorkAreaCanContainIt()
    {
        var fit=WindowViewport.Fit(1920,1040);
        Assert.Equal(1260,fit.Width);
        Assert.Equal(730,fit.Height);
    }

    [Theory]
    [InlineData(0,730)][InlineData(1260,-1)][InlineData(double.NaN,730)]
    [InlineData(1260,double.PositiveInfinity)]
    public void InvalidMonitorMetricsAreRejected(double width,double height)
        =>Assert.Throws<ArgumentOutOfRangeException>(()=>WindowViewport.Fit(width,height));
}
