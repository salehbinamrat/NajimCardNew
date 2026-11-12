using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

public sealed class UserManagerProfileLimitationBindingTests
{
    private sealed class Router : IRouterCommandClient
    {
        public readonly List<string> Paths=[];
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            Paths.Add(command);
            IReadOnlyList<IReadOnlyDictionary<string,string>> result=command switch
            {
                "/system/resource/print" =>
                    [new Dictionary<string,string>{{"version","7.21"}}],
                "/user-manager/profile/print" =>
                    [new Dictionary<string,string>{{"name","monthly"}}],
                "/user-manager/limitation/print" =>
                    [new Dictionary<string,string>{{"name","10GB"}}],
                "/user-manager/profile-limitation/print" => [],
                _=>throw new InvalidOperationException(command)
            };
            return Task.FromResult(result);
        }
    }

    [Fact]
    public async Task WithoutReceiptAndExactReadback_LinkMustNotWrite()
    {
        var router=new Router();
        await Assert.ThrowsAsync<NotSupportedException>(()=>
            new RouterGateway(router).LinkUmProfileLimitationAsync("monthly","10GB"));
        Assert.DoesNotContain(router.Paths,x=>x.EndsWith("/add"));
    }

    [Theory]
    [InlineData("","10GB")]
    [InlineData("monthly","")]
    public async Task InvalidRelation_FailsBeforeWrite(string profile,string limit)
    {
        var router=new Router();
        await Assert.ThrowsAsync<ArgumentException>(()=>
            new RouterGateway(router).LinkUmProfileLimitationAsync(profile,limit));
        Assert.Empty(router.Paths);
    }
}
