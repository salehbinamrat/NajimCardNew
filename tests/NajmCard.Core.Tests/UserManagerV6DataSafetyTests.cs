using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

// Entirely in-memory RouterOS boundary: no credentials, sockets or device writes.
public sealed class UserManagerV6DataSafetyTests
{
    private const long MiB=1024L*1024;
    private static Dictionary<string,string> Row(params string[] pairs)
    {
        var row=new Dictionary<string,string>(StringComparer.Ordinal);
        for(int i=0;i<pairs.Length;i+=2)row.Add(pairs[i],pairs[i+1]);
        return row;
    }

    private sealed class Router : IRouterCommandClient,IRouterQueryClient
    {
        public Dictionary<string,string> User=Row(".id","*A","username","card",
            "customer","alice","actual-profile","gold","disabled","false",
            "incomplete","false","download-used","500MiB","upload-used","0");
        public List<Dictionary<string,string>> Profiles=[
            Row(".id","*10","name","gold","owner","alice"),
            Row(".id","*20","name","gold","owner","bob")];
        public List<Dictionary<string,string>> Limits=[
            Row(".id","*11","name","quota","owner","alice","transfer-limit","1000MiB"),
            Row(".id","*21","name","quota","owner","bob","transfer-limit","100MiB")];
        public List<Dictionary<string,string>> Links=[
            Row("profile","*10","limitation","*11"),
            Row("profile","*20","limitation","*21")];
        public int UserReads,TargetReads;
        public List<string> Removed=[];
        public Action? BeforeTargetRead;
        public Exception? TargetError;

        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(command=="/tool/user-manager/user/remove")
            {
                Removed.Add(attributes![".id"]);
                return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>([]);
            }
            if(command=="/tool/user-manager/user/print")UserReads++;
            IEnumerable<Dictionary<string,string>> rows=command switch
            {
                "/system/resource/print"=>[Row("version","6.49.19")],
                "/tool/user-manager/user/print"=>[User],
                "/tool/user-manager/profile/print"=>Profiles,
                "/tool/user-manager/profile/limitation/print"=>Limits,
                "/tool/user-manager/profile/profile-limitation/print"=>Links,
                _=>throw new InvalidOperationException("Unexpected command: "+command)
            };
            var fields=attributes?.GetValueOrDefault(".proplist")?.Split(',').ToHashSet();
            return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>(
                rows.Select(row=>(IReadOnlyDictionary<string,string>)row
                    .Where(p=>fields is null||fields.Contains(p.Key)).ToDictionary()).ToArray());
        }

        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> QueryByIdAsync(
            string command,string id,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            Assert.Equal("/tool/user-manager/user/print",command);
            TargetReads++;
            if(TargetError is not null)throw TargetError;
            BeforeTargetRead?.Invoke();
            return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>(
                id==User[".id"]?[new Dictionary<string,string>(User)]:[]);
        }
    }

    [Theory]
    [InlineData("gold")]
    [InlineData("*10")]
    public async Task SameNamedProfilesAndLimitations_KeepTheCorrectOwnersQuota(string reference)
    {
        var router=new Router();router.User["actual-profile"]=reference;
        var inventory=await new RouterGateway(router).ReadUserManagerV6InventoryAsync();
        var card=Assert.Single(inventory.Users);
        Assert.True(inventory.IsComplete,inventory.Diagnostic);
        Assert.Equal(1000*MiB,card.TransferLimit);
        Assert.Equal(500*MiB,card.ConsumedBytes);
        Assert.Equal("active",card.Status);
        var result=await new RouterGateway(router).DeleteExpiredUserManagerV6Async(inventory.Users);
        Assert.Equal(0,result.Deleted);Assert.Empty(router.Removed);
    }

    [Theory]
    [InlineData("user")]
    [InlineData("profile")]
    [InlineData("limitation")]
    public async Task MissingOwnership_DoesNotCertifyExpiry(string table)
    {
        var router=new Router();router.User["download-used"]="2000MiB";
        if(table=="user")router.User.Remove("customer");
        if(table=="profile")router.Profiles[0].Remove("owner");
        if(table=="limitation")router.Limits[0].Remove("owner");
        var inventory=await new RouterGateway(router).ReadUserManagerV6InventoryAsync();
        Assert.False(inventory.IsComplete);
        Assert.DoesNotContain(inventory.Users,x=>x.Status=="expired");
    }

    [Fact]
    public async Task BareNameLinks_ResolveWithinTheirExplicitOwner()
    {
        var router=new Router();router.Links=[
            Row("profile","gold","limitation","quota","owner","alice"),
            Row("profile","gold","limitation","quota","owner","bob")];
        var inventory=await new RouterGateway(router).ReadUserManagerV6InventoryAsync();
        Assert.True(inventory.IsComplete,inventory.Diagnostic);
        Assert.Equal(1000*MiB,Assert.Single(inventory.Users).TransferLimit);
    }

    [Fact]
    public async Task AmbiguousBareNameLink_AndCrossOwnerIdLink_AreNotCertified()
    {
        foreach(var link in new[]{Row("profile","gold","limitation","quota"),
            Row("profile","*10","limitation","*21")})
        {
            var router=new Router();router.User["download-used"]="2000MiB";
            router.Links=[link];
            var inventory=await new RouterGateway(router).ReadUserManagerV6InventoryAsync();
            Assert.False(inventory.IsComplete);
            Assert.DoesNotContain(inventory.Users,x=>x.Status=="expired");
        }
    }

    [Theory]
    [InlineData("disabled",null)]
    [InlineData("incomplete",null)]
    [InlineData("disabled","unknown")]
    [InlineData("incomplete","bad")]
    public async Task UnverifiedAdministrativeFlags_NeverAuthorizeClassificationOrDeletion(
        string field,string? value)
    {
        var router=new Router();router.User["download-used"]="2000MiB";
        var gateway=new RouterGateway(router);
        var confirmed=await gateway.ReadUserManagerV6InventoryAsync();
        if(value is null)router.User.Remove(field);else router.User[field]=value;
        var incomplete=await gateway.ReadUserManagerV6InventoryAsync();
        Assert.False(incomplete.IsComplete);
        Assert.DoesNotContain(incomplete.Users,x=>x.Status=="expired");
        var account=Assert.Single((await gateway.ReadUmAccountsAsync()).Rows);
        Assert.Null(account.Disabled);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(()=>
            gateway.DeleteExpiredUserManagerV6Async(confirmed.Users));
        Assert.Empty(router.Removed);
    }

    [Theory]
    [InlineData("download-used","bad")]
    [InlineData("upload-used","")]
    public async Task InvalidLegacyPair_UsesTheSameValidatedAlternativePairForBothCounters(
        string field,string value)
    {
        var router=new Router();router.User[field]=value;
        router.User["total-download-used"]="1500MiB";
        router.User["total-upload-used"]="500MiB";
        var inventory=await new RouterGateway(router).ReadUserManagerV6InventoryAsync();
        var card=Assert.Single(inventory.Users);
        Assert.True(inventory.IsComplete,inventory.Diagnostic);
        Assert.Equal(1500*MiB,card.DownloadBytes);Assert.Equal(500*MiB,card.UploadBytes);
        Assert.Equal("expired",card.Status);
    }

    [Fact]
    public async Task CannotMixIndividuallyValidCountersFromTwoInvalidPairs()
    {
        var router=new Router();router.User["download-used"]="bad";
        router.User["total-download-used"]="2000MiB";router.User["total-upload-used"]="bad";
        var inventory=await new RouterGateway(router).ReadUserManagerV6InventoryAsync();
        Assert.False(inventory.IsComplete);
        Assert.DoesNotContain(inventory.Users,x=>x.Status=="expired");
    }

    [Theory]
    [InlineData("missing-link")]
    [InlineData("malformed-limit")]
    [InlineData("missing-limit")]
    public async Task UnresolvedOrMalformedLimits_AreUnknownRatherThanUnlimited(string fault)
    {
        var router=new Router();router.User["download-used"]="2000MiB";
        if(fault=="missing-link")router.Links[0]["limitation"]="*999";
        if(fault=="malformed-limit")router.Limits[0]["transfer-limit"]="garbage";
        if(fault=="missing-limit")router.Limits[0].Remove("transfer-limit");
        var inventory=await new RouterGateway(router).ReadUserManagerV6InventoryAsync();
        Assert.False(inventory.IsComplete);Assert.NotEmpty(inventory.Diagnostic);
        Assert.DoesNotContain(inventory.Users,x=>x.Status=="expired");
    }

    [Theory]
    [InlineData(null,null)]
    [InlineData("bad",null)]
    [InlineData("0",0)]
    [InlineData("2",2)]
    public async Task AccountsExposeOnlyVerifiedSessionCounts(string? raw,int? expected)
    {
        var router=new Router();if(raw is not null)router.User["active-sessions"]=raw;
        var accounts=await new RouterGateway(router).ReadUmAccountsAsync();
        Assert.Equal(expected,Assert.Single(accounts.Rows).ActiveSessions);
        var card=Assert.Single((await new RouterGateway(router).ReadUserManagerV6InventoryAsync()).Users);
        Assert.Equal(expected?.ToString()??"غير متاح",card.ActiveSessionsLabel);
    }

    [Theory]
    [InlineData("renewed")]
    [InlineData("owner")]
    [InlineData("quota")]
    public async Task CleanupRefreshesUserAndDefinitionsInsteadOfTrustingTheSelectedSnapshot(string change)
    {
        var router=new Router();router.User["download-used"]="2000MiB";
        var gateway=new RouterGateway(router);
        var selected=await gateway.ReadCachedUserManagerV6InventoryAsync();
        Assert.Equal("expired",Assert.Single(selected.Users).Status);
        if(change=="renewed")router.User["download-used"]="10MiB";
        if(change=="owner")router.User["customer"]="bob";
        if(change=="quota")router.Limits[0]["transfer-limit"]="3000MiB";
        var result=await gateway.DeleteExpiredUserManagerV6Async(selected.Users);
        Assert.Equal(0,result.Deleted);Assert.Empty(router.Removed);
        Assert.True(router.UserReads>=2);
    }

    [Fact]
    public async Task CleanupRejectsFreshMissingDefinitionsEvenAfterACompleteCachedRead()
    {
        var router=new Router();router.User["download-used"]="2000MiB";
        var gateway=new RouterGateway(router);
        var selected=await gateway.ReadCachedUserManagerV6InventoryAsync();
        router.Links[0]["limitation"]="*999";
        await Assert.ThrowsAnyAsync<InvalidOperationException>(()=>
            gateway.DeleteExpiredUserManagerV6Async(selected.Users));
        Assert.Empty(router.Removed);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("incomplete")]
    [InlineData("renewed")]
    public async Task CleanupChecksTheRawTargetAgainImmediatelyBeforeRemoval(string change)
    {
        var router=new Router();router.User["download-used"]="2000MiB";
        var gateway=new RouterGateway(router);
        var selected=await gateway.ReadUserManagerV6InventoryAsync();
        router.BeforeTargetRead=()=>
        {
            if(change=="renewed")router.User["download-used"]="0";
            else router.User.Remove(change);
        };
        var result=await gateway.DeleteExpiredUserManagerV6Async(selected.Users);
        Assert.Equal(0,result.Deleted);Assert.Empty(router.Removed);
        Assert.True(router.TargetReads>0);
    }

    [Fact]
    public async Task FullyVerifiedExpiredCard_IsRemovedOnlyAfterFreshTargetRead()
    {
        var router=new Router();router.User["download-used"]="2000MiB";
        var gateway=new RouterGateway(router);
        var selected=await gateway.ReadUserManagerV6InventoryAsync();
        var result=await gateway.DeleteExpiredUserManagerV6Async(selected.Users);
        Assert.Equal(1,result.Deleted);Assert.Equal(["*A"],router.Removed);
        Assert.Equal(1,router.TargetReads);
    }

    [Fact]
    public async Task CleanupRechecksDefinitionsChangedDuringTheBatch()
    {
        var router=new Router();router.User["download-used"]="2000MiB";
        var gateway=new RouterGateway(router);
        var selected=await gateway.ReadUserManagerV6InventoryAsync();
        router.BeforeTargetRead=()=>router.Limits[0]["transfer-limit"]="3000MiB";
        var result=await gateway.DeleteExpiredUserManagerV6Async(selected.Users);
        Assert.Equal(0,result.Deleted);Assert.Empty(router.Removed);
    }

    [Fact]
    public async Task PlanDisplayAndSummariesKeepOwnerScopedAssociations()
    {
        var router=new Router();
        router.Limits[0]["name"]="alice-quota";router.Limits[1]["name"]="bob-quota";
        var gateway=new RouterGateway(router);
        var plans=await gateway.ReadUmPlansAsync();
        Assert.True(plans.IsComplete,plans.Diagnostic);
        Assert.Equal("alice-quota",Assert.Single(plans.Rows,x=>x.Kind=="Profile"&&x.Owner=="alice").AssociatedLimits);
        Assert.Equal("bob-quota",Assert.Single(plans.Rows,x=>x.Kind=="Profile"&&x.Owner=="bob").AssociatedLimits);
        var summaries=await gateway.ReadUserManagerV6ProfileLimitSummaryAsync();
        Assert.Equal(1000*MiB,Assert.Single(summaries,x=>x.ProfileId=="*10").EffectiveTransferLimitBytes);
        Assert.Equal(100*MiB,Assert.Single(summaries,x=>x.ProfileId=="*20").EffectiveTransferLimitBytes);
    }

    [Theory]
    [InlineData(".id")]
    [InlineData("username")]
    public async Task IncompleteIdentityIsNeverClassifiedAsExpired(string field)
    {
        var router=new Router();router.User["download-used"]="2000MiB";
        router.User.Remove(field);
        var inventory=await new RouterGateway(router).ReadUserManagerV6InventoryAsync();
        Assert.False(inventory.IsComplete);
        var card=Assert.Single(inventory.Users);
        Assert.Equal("unclassified",card.Status);
        Assert.False(card.CanDeleteAsTransferExpired);
    }

    [Fact]
    public async Task UnknownQuotaAndUsageAreExplicitInDetailLabels()
    {
        var router=new Router();router.User["download-used"]="bad";
        router.Links[0]["limitation"]="*missing";
        var card=Assert.Single((await new RouterGateway(router).ReadUserManagerV6InventoryAsync()).Users);
        Assert.Equal("غير مؤكد",card.TransferLimitLabel);
        Assert.Equal("غير مؤكد",card.RemainingLabel);
        Assert.Equal("غير متاح",card.ConsumedBytesLabel);
        Assert.Equal("غير متاح",card.DownloadBytesLabel);
        Assert.Equal("غير متاح",card.UploadBytesLabel);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedIndependentReadDoesNotReturnAnOldVerifiedInventoryOrAnUncertainWrite(bool trap)
    {
        var router=new Router();router.User["download-used"]="2000MiB";
        var gateway=new RouterGateway(router);
        var selected=await gateway.ReadUserManagerV6InventoryAsync();
        router.TargetError=trap?new RouterOsTrapException("denied"):new IOException("read disconnected");
        var result=await gateway.DeleteExpiredUserManagerV6Async(selected.Users);
        Assert.Empty(router.Removed);Assert.Equal(0,result.Deleted);
        Assert.False(result.UncertainOutcome); // No remove command was attempted.
        Assert.Null(result.RemainingUserManager);
        Assert.Contains("card",result.Failed);
    }

    [Fact]
    public async Task KnownQuotaWithUnknownUsageCannotDisplayARemainingBalance()
    {
        var router=new Router();router.User["download-used"]="bad";
        var card=Assert.Single((await new RouterGateway(router).ReadUserManagerV6InventoryAsync()).Users);
        Assert.Equal("غير مؤكد",card.RemainingLabel);
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("actual-profile")]
    [InlineData("link-owner")]
    public async Task CaseDistinctOwnersAndProfileNamesMustNotAlias(string field)
    {
        var router=new Router();router.User["download-used"]="2000MiB";
        if(field=="link-owner")router.Links[0]["owner"]="ALICE";
        else router.User[field]=router.User[field].ToUpperInvariant();
        var inventory=await new RouterGateway(router).ReadUserManagerV6InventoryAsync();
        Assert.False(inventory.IsComplete);
        Assert.DoesNotContain(inventory.Users,x=>x.Status=="expired");
    }
}
