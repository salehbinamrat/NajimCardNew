using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

public sealed class UserManagerReadProjectionPerformanceTests
{
    private sealed class LegacyRouter : IRouterCommandClient, IRouterReceiptClient, IRouterQueryClient
    {
        public int ProjectedReads;
        public int FullExactIdReads;
        public int Adds;
        public string Username="";
        public string Profile="";

        public Task<RouterCommandReply> CommandWithReceiptAsync(
            string command,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            if(command=="/tool/user-manager/user/add")
            {
                Adds++;
                Username=attributes!["username"];
                return Task.FromResult(new RouterCommandReply([],
                    new Dictionary<string,string>{{"ret","*"+Adds.ToString("X")}}));
            }
            throw new InvalidOperationException("Unexpected receipt: "+command);
        }

        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            Assert.Equal("/tool/user-manager/user/create-and-activate-profile",command);
            Profile=attributes!["profile"];
            return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>([]);
        }

        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> QueryByIdAsync(
            string command,string id,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            Assert.Equal("/tool/user-manager/user/print",command);
            var data=new Dictionary<string,string>
            {
                [".id"]=id,["customer"]="admin"
            };
            if(attributes is null)
            {
                FullExactIdReads++;
                data["username"]=Username;
                data["actual-profile"]=Profile;
            }
            else
            {
                // Simulate RouterOS 6 silently excluding projected columns.
                ProjectedReads++;
            }
            return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>([data]);
        }
    }

    [Fact]
    public async Task LegacyProjectionFallback_IsLearnedOnce_AndAlwaysChecksExactId()
    {
        var fake=new LegacyRouter();
        var gateway=new RouterGateway(fake);
        for(int i=0;i<5;i++)
        {
            var voucher=new Voucher("card"+i,"","gold","batch",
                DateTimeOffset.UtcNow,Service:"userman",Customer:"admin");
            var result=await gateway.CreateAndActivateUserManagerV6Async(voucher,"admin");
            Assert.True(result.Success);
            Assert.False(result.UncertainOutcome);
        }
        Assert.Equal(5,fake.Adds);
        Assert.Equal(1,fake.ProjectedReads);
        Assert.Equal(5,fake.FullExactIdReads);
    }
}
