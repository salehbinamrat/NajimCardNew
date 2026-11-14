using System.Globalization;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>Tests-first safeguards for trustworthy RouterOS counts and low-cost reads.</summary>
public sealed class ReliableRouterDataPerformanceTests
{
    private sealed class V6Router : IRouterCommandClient
    {
        public int UserReads;
        public bool MissingUsageOnSecondRow = true;
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command, IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<IReadOnlyDictionary<string,string>> rows=command switch
            {
                "/system/resource/print" =>
                    [new Dictionary<string,string> {["version"]="6.49.19"}],
                "/tool/user-manager/user/print" => ReadUsers(),
                "/tool/user-manager/profile/print" or
                "/tool/user-manager/profile/limitation/print" or
                "/tool/user-manager/profile/profile-limitation/print" => [],
                _=>throw new InvalidOperationException("Unexpected command: "+command)
            };
            return Task.FromResult(rows);
        }
        private IReadOnlyList<IReadOnlyDictionary<string,string>> ReadUsers()
        {
            UserReads++;
            var first=new Dictionary<string,string>
            {
                [".id"]="*1",["username"]="card-1",["actual-profile"]="gold",
                ["total-download-used"]="400",["total-upload-used"]="100",
                ["disabled"]="false"
            };
            var second=new Dictionary<string,string>
            {
                [".id"]="*2",["username"]="card-2",["actual-profile"]="gold",
                ["disabled"]="false"
            };
            if(!MissingUsageOnSecondRow)
            {
                second["total-download-used"]="0";
                second["total-upload-used"]="0";
            }
            return [first,second];
        }
    }

    [Fact]
    public async Task MixedV6RowsWithMissingCounters_AreRetriedAndNotCertified()
    {
        var router=new V6Router();
        var inventory=await new RouterGateway(router).ReadUserManagerV6InventoryAsync();
        Assert.Equal(2,router.UserReads); // partial projection retried once unprojected
        Assert.False(inventory.IsComplete);
        Assert.Contains("غير مكتملة",inventory.Diagnostic);
        Assert.Equal(2,inventory.Total); // rows remain inspectable, never invented
    }

    [Fact]
    public async Task IncompleteV6AccountSnapshot_HasUncertainStatusAndUnknownUsage()
    {
        var router=new V6Router();
        var snapshot=await new RouterGateway(router).ReadUmAccountsAsync();
        Assert.False(snapshot.IsComplete);
        Assert.Contains("غير مكتملة",snapshot.Diagnostic);
        Assert.Equal(2,snapshot.Rows.Count);
        Assert.All(snapshot.Rows,row=>
        {
            Assert.Equal("غير مؤكد",row.Status);
            Assert.Null(row.DownloadBytes);
            Assert.Null(row.UploadBytes);
            Assert.Null(row.ActiveSessions);
        });
    }

    [Fact]
    public async Task CompleteV6AccountSnapshot_StillUsesOriginalLifecycleClassification()
    {
        var router=new V6Router { MissingUsageOnSecondRow=false };
        var snapshot=await new RouterGateway(router).ReadUmAccountsAsync();
        Assert.True(snapshot.IsComplete);
        Assert.Equal(1,router.UserReads);
        Assert.Equal("استخدم",Assert.Single(snapshot.Rows.Where(x=>x.Name=="card-1")).Status);
        Assert.Equal("صالح",Assert.Single(snapshot.Rows.Where(x=>x.Name=="card-2")).Status);
    }

    private sealed class VersionRouter : IRouterCommandClient
    {
        public int ResourceReads;
        public string Version="6.49.19";
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(command!="/system/resource/print")
                throw new InvalidOperationException("Unexpected read: "+command);
            Interlocked.Increment(ref ResourceReads);
            IReadOnlyList<IReadOnlyDictionary<string,string>> rows=
                [new Dictionary<string,string> {["version"]=Version}];
            return Task.FromResult(rows);
        }
    }

    [Fact]
    public async Task VersionCache_CoalescesRepeatReads_AndForceRechecksRouter()
    {
        var router=new VersionRouter();
        var gateway=new RouterGateway(router);
        var results=await Task.WhenAll(Enumerable.Range(0,12)
            .Select(_=>gateway.ReadCachedRouterOsGenerationAsync()));
        Assert.All(results,version=>Assert.Equal(RouterOsGeneration.V6,version));
        Assert.Equal(1,router.ResourceReads);
        router.Version="7.21";
        Assert.Equal(RouterOsGeneration.V7,
            await gateway.ReadCachedRouterOsGenerationAsync(force:true));
        Assert.Equal(2,router.ResourceReads);
        Assert.Equal(RouterOsGeneration.V7,
            await gateway.ReadCachedRouterOsGenerationAsync());
        Assert.Equal(2,router.ResourceReads);
    }

    [Fact]
    public async Task VersionCache_DoesNotCacheInvalidOrUnsupportedVersions()
    {
        var router=new VersionRouter { Version="8.0" };
        var gateway=new RouterGateway(router);
        await Assert.ThrowsAsync<NotSupportedException>(()=>
            gateway.ReadCachedRouterOsGenerationAsync());
        router.Version="6.49.19";
        Assert.Equal(RouterOsGeneration.V6,
            await gateway.ReadCachedRouterOsGenerationAsync());
        Assert.Equal(2,router.ResourceReads);
    }

    private sealed class PlansRouter : IRouterCommandClient
    {
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            IReadOnlyList<IReadOnlyDictionary<string,string>> rows=command switch
            {
                "/system/resource/print" => [new Dictionary<string,string> {["version"]="7.21"}],
                "/user-manager/profile/print" =>
                    [new Dictionary<string,string>{{".id","*1"},{"name","Silver"}},
                     new Dictionary<string,string>{{".id","*2"},{"name","Gold"}}],
                "/user-manager/limitation/print" =>
                    [new Dictionary<string,string>{{".id","*3"},{"name","LimitB"}},
                     new Dictionary<string,string>{{".id","*4"},{"name","LimitA"}}],
                "/user-manager/profile-limitation/print" =>
                    [new Dictionary<string,string>{{"profile","Gold"},{"limitation","LimitA"}},
                     new Dictionary<string,string>{{"profile","Silver"},{"limitation","LimitA"}},
                     new Dictionary<string,string>{{"profile","Gold"},{"limitation","LimitB"}},
                     new Dictionary<string,string>{{"profile","Gold"},{"limitation","LimitA"}}],
                _=>throw new InvalidOperationException(command)
            };
            return Task.FromResult(rows);
        }
    }

    [Fact]
    public async Task ReverseLimitationIndex_PreservesSortedUniqueProfileLinks()
    {
        var data=await new RouterGateway(new PlansRouter()).ReadUmPlansAsync();
        Assert.True(data.IsComplete);
        var rows=data.Rows.Where(x=>x.Kind=="Limitation").ToArray();
        Assert.Equal("Gold، Silver",Assert.Single(rows.Where(x=>x.Name=="LimitA")).AssociatedLimits);
        Assert.Equal("Gold",Assert.Single(rows.Where(x=>x.Name=="LimitB")).AssociatedLimits);
        Assert.Equal("LimitA، LimitB",Assert.Single(data.Rows.Where(x=>x.Name=="Gold")).AssociatedLimits);
    }
}
