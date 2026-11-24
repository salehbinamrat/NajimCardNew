using System.Runtime.InteropServices;
using Xunit;

namespace NajmCard.Desktop.Tests;

public sealed class ReleaseArchitectureStage6Tests
{
    [Fact]
    public void WindowsTestsActuallyRunInTheRequestedNativeArchitecture()
    {
        Assert.True(OperatingSystem.IsWindows());
        var expected=Environment.GetEnvironmentVariable("NAJM_EXPECT_ARCH");
        if(expected is null)return; // Local Windows runs still exercise the native runtime.
        Assert.Contains(expected,new[]{"x86","x64"});
        Assert.Equal(expected=="x86"?Architecture.X86:Architecture.X64,
            RuntimeInformation.ProcessArchitecture);
        Assert.Equal(expected=="x86"?4:8,IntPtr.Size);
    }
}
