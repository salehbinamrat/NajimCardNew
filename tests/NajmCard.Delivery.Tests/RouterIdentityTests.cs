using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Delivery.Tests;

public sealed class RouterIdentityTests
{
    private sealed class Device(string serial,string systemId,string softwareId) : IRouterCommandClient
    {
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(string command,
            IReadOnlyDictionary<string,string>? attributes=null,CancellationToken cancellationToken=default)=>
            Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>(
                [command=="/system/routerboard/print"?new Dictionary<string,string>{{"serial-number",serial}}:
                    new Dictionary<string,string>{{"system-id",systemId},{"software-id",softwareId}}]);
    }
    [Theory]
    [InlineData("device1","", "","rb:")]
    [InlineData("","chr-device", "","chr:")]
    [InlineData("","", "ABCD-1234","sw:")]
    public async Task UsesStableHardwareOrInstallationIdentity(string serial,string system,string software,string prefix)
    {
        var id=await RouterDeviceIdentity.ReadAsync(new Device(serial,system,software));
        Assert.StartsWith(prefix,id);Assert.Equal(prefix.Length+64,id.Length);
        Assert.Equal(id,await RouterDeviceIdentity.ReadAsync(new Device(serial,system,software)));
    }
    [Theory]
    [InlineData("")][InlineData("unknown")][InlineData("00000000")][InlineData("N/A")]
    public async Task PlaceholderIdentityCannotBindABatch(string serial)=>
        await Assert.ThrowsAsync<InvalidOperationException>(()=>RouterDeviceIdentity.ReadAsync(new Device(serial,"","")));
}
