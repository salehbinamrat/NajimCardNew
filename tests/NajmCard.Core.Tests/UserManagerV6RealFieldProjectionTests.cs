using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>
/// RouterOS v6 actually reports download-used/upload-used for User Manager.
/// A projected read must include these; a full sensitive inventory on each
/// dashboard visit is too expensive for thousands of subscribers.
/// </summary>
public sealed class UserManagerV6RealFieldProjectionTests
{
    private sealed class V6Router : IRouterCommandClient
    {
        public int ProjectedReads;
        public int FullReads;
        public bool MalformedCounters;
        public bool BlockLimits;
        public bool DecimalZeroCounters;
        public bool BothLegacyAndTotals;
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string path,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(path=="/tool/user-manager/user/print")
            {
                if(attributes is null)FullReads++;
                else ProjectedReads++;
                var projection=attributes is null ? null :
                    attributes.TryGetValue(".proplist",out var text)
                        ?new HashSet<string>(text.Split(','),StringComparer.Ordinal)
                        :new HashSet<string>(StringComparer.Ordinal);
                var result=new List<IReadOnlyDictionary<string,string>>();
                foreach(var (id,name,download,upload) in new[]{
                    ("*1","card-active","35","15"),
                    ("*2","card-expired","70","50"),
                    ("*3","card-unused","0","0")})
                {
                    var source=new Dictionary<string,string>{
                        [".id"]=id,["username"]=name,
                        ["actual-profile"]="silver",["disabled"]="false",
                        ["download-used"]=MalformedCounters?"bad":
                            DecimalZeroCounters && name=="card-unused"?"0.0":download,
                        ["upload-used"]=upload,["uptime-used"]=name=="card-unused"?"0s":"2m"
                    };
                    if(BothLegacyAndTotals)
                    {
                        source["total-download-used"]="0";
                        source["total-upload-used"]="0";
                    }
                    result.Add(projection is null ?source:
                        source.Where(p=>projection.Contains(p.Key))
                              .ToDictionary(p=>p.Key,p=>p.Value));
                }
                return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>(result);
            }
            if(BlockLimits && path=="/tool/user-manager/profile/print")
                throw new RouterOsTrapException("not enough permissions");
            IReadOnlyList<IReadOnlyDictionary<string,string>> rows=path switch{
                "/tool/user-manager/profile/print" =>
                    [new Dictionary<string,string>{{".id","*P"},{"name","silver"}}],
                "/tool/user-manager/profile/limitation/print" =>
                    [new Dictionary<string,string>{{".id","*L"},{"name","silver-limit"},{"transfer-limit","100"}}],
                "/tool/user-manager/profile/profile-limitation/print" =>
                    [new Dictionary<string,string>{{"profile","*P"},{"limitation","*L"}}],
                _=>throw new InvalidOperationException(path)
            };
            return Task.FromResult(rows);
        }
    }

    [Fact]
    public async Task V6LegacyCounters_CompleteInOneProjectedReadAndClassifyTransferLimit()
    {
        var router=new V6Router();
        var report=await new RouterGateway(router).ReadUserManagerV6InventoryAsync();
        Assert.Equal(1,router.ProjectedReads);
        Assert.Equal(0,router.FullReads);
        Assert.True(report.IsComplete,report.Diagnostic);
        Assert.Equal(3,report.Total);
        Assert.Equal(1,report.Active);
        Assert.Equal(1,report.Expired);
        Assert.Equal(1,report.Valid);
        Assert.Equal(2,report.Users.Count(x=>x.Started));
        Assert.Equal("transfer-limit",report.Users.Single(x=>x.Status=="expired").ExpirySource);
    }

    [Fact]
    public async Task CorruptLegacyUsage_DoesNotBecomeVerifiedZeroOrActive()
    {
        var router=new V6Router{MalformedCounters=true};
        var report=await new RouterGateway(router).ReadUserManagerV6InventoryAsync();
        Assert.False(report.IsComplete);
        Assert.Equal(1,router.ProjectedReads);
        Assert.Equal(1,router.FullReads);
    }

    [Fact]
    public async Task DecimalZeroCounters_DoNotRequireWholeDatabaseFallback()
    {
        var router=new V6Router{DecimalZeroCounters=true};
        var report=await new RouterGateway(router).ReadUserManagerV6InventoryAsync();
        Assert.True(report.IsComplete,report.Diagnostic);
        Assert.Equal(1,router.ProjectedReads);
        Assert.Equal(0,router.FullReads);
        Assert.Equal(1,report.Expired);
    }

    [Fact]
    public async Task WhenBothFormatsExist_UserManagerV6LegacyUsageIsAuthoritative()
    {
        var router=new V6Router{BothLegacyAndTotals=true};
        var report=await new RouterGateway(router).ReadUserManagerV6InventoryAsync();
        Assert.True(report.IsComplete,report.Diagnostic);
        Assert.Equal(1,report.Active);
        Assert.Equal(1,report.Expired);
        Assert.Equal(1,report.Valid);
    }

    [Fact]
    public async Task UnavailablePackageTransferLimits_NeverCertifyExpiry()
    {
        var router=new V6Router{BlockLimits=true};
        var report=await new RouterGateway(router).ReadUserManagerV6InventoryAsync();
        Assert.False(report.IsComplete);
        Assert.Contains("حدود",report.Diagnostic);
    }
}
