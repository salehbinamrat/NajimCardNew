using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>All rows synthetic; reads are verified without a router or credentials.</summary>
public sealed class UserManagerCreatorCacheAndSessionIndicatorTests
{
    private sealed class FakeRouter : IRouterCommandClient
    {
        public int Profiles {get;private set;}
        public int Customers {get;private set;}
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            if(command=="/tool/user-manager/profile/print")
            {
                Profiles++;
                IReadOnlyList<IReadOnlyDictionary<string,string>> rows=
                    [new Dictionary<string,string>
                    {
                        [".id"]="*1",["name"]="200RY",["owner"]="admin",
                        ["price"]="200",["validity"]="1d",["starts-at"]="first-auth"
                    }];
                return Task.FromResult(rows);
            }
            if(command=="/tool/user-manager/customer/print")
            {
                Customers++;
                IReadOnlyList<IReadOnlyDictionary<string,string>> rows=
                    [new Dictionary<string,string>
                    {
                        [".id"]="*2",["name"]="admin",["login"]="admin"
                    }];
                return Task.FromResult(rows);
            }
            throw new InvalidOperationException("Unexpected RouterOS read: "+command);
        }
    }

    [Fact]
    public async Task CreatorCatalogDoesNotRepeatRouterReadsOnPageReentry()
    {
        var client=new FakeRouter();
        var gateway=new RouterGateway(client);
        Assert.Single(await gateway.ReadCachedCreatorProfilesAsync());
        Assert.Single(await gateway.ReadCachedCreatorCustomersAsync());
        Assert.Single(await gateway.ReadCachedCreatorProfilesAsync());
        Assert.Single(await gateway.ReadCachedCreatorCustomersAsync());
        Assert.Equal(1,client.Profiles);
        Assert.Equal(1,client.Customers);
        Assert.Single(await gateway.ReadCachedCreatorProfilesAsync(force:true));
        Assert.Single(await gateway.ReadCachedCreatorCustomersAsync(force:true));
        Assert.Equal(2,client.Profiles);
        Assert.Equal(2,client.Customers);
    }

    [Fact]
    public void MissingSessionCounterMustNeverBePresentedAsConfirmedZero()
    {
        var row=new Dictionary<string,string>
        {
            [".id"]="*A",["username"]="1234",["actual-profile"]="200RY",
            ["total-download-used"]="1024"
        };
        var missing=UserManagerV6Usage.Classify(row,2048);
        Assert.False(missing.ActiveSessionsVerified);
        row["active-sessions"]="3";
        var known=UserManagerV6Usage.Classify(row,2048);
        Assert.True(known.ActiveSessionsVerified);
        Assert.Equal(3,known.ActiveSessions);
        row["active-sessions"]="0";
        var zero=UserManagerV6Usage.Classify(row,2048);
        Assert.True(zero.ActiveSessionsVerified);
        Assert.Equal(0,zero.ActiveSessions);
    }
}
