using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

public sealed class UserManagerCatalogVersionTests
{
    private sealed class Spy(string version) : IRouterCommandClient
    {
        public readonly List<string> Paths = [];
        public readonly List<IReadOnlyDictionary<string, string>?> Arguments = [];
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command, IReadOnlyDictionary<string,string>? attributes = null,
            CancellationToken cancellationToken = default)
        {
            Paths.Add(command);
            Arguments.Add(attributes);
            if (command == "/system/resource/print")
                return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>(
                    [new Dictionary<string,string> { ["version"] = version }]);
            IReadOnlyList<IReadOnlyDictionary<string,string>> result = command switch
            {
                "/tool/user-manager/profile/print" =>
                    [new Dictionary<string,string> { [".id"]="*1",["name"]="six-profile",["validity"]="1d",["price"]="200" }],
                "/tool/user-manager/profile/limitation/print" =>
                    [new Dictionary<string,string> { [".id"]="*2",["name"]="six-limit",["transfer-limit"]="1GiB" }],
                "/tool/user-manager/session/print" =>
                    [new Dictionary<string,string> { [".id"]="*3",["user"]="six-user",["uptime"]="2m" }],
                "/user-manager/user/print" =>
                    [new Dictionary<string,string> { [".id"]="*4",["name"]="seven-user",["disabled"]="no" }],
                "/user-manager/profile/print" =>
                    [new Dictionary<string,string> { [".id"]="*5",["name"]="seven-profile",["starts-when"]="first-auth" }],
                "/user-manager/limitation/print" =>
                    [new Dictionary<string,string> { [".id"]="*6",["name"]="seven-limit",["download-limit"]="2G" }],
                "/user-manager/session/print" =>
                    [new Dictionary<string,string> { [".id"]="*7",["user"]="seven-user",["user-address"]="10.0.0.9",["download"]="500" }],
                _ => throw new RouterOsTrapException("Unsupported menu: "+command)
            };
            return Task.FromResult(result);
        }
    }

    [Fact]
    public async Task VersionSix_UsesLegacyProfileAndLimitationMenus()
    {
        var spy=new Spy("6.49.19 (stable)");
        var api=new RouterGateway(spy);
        var profiles=await api.ReadUserManagerCatalogAsync(UserManagerCatalogSection.Profiles);
        var limits=await api.ReadUserManagerCatalogAsync(UserManagerCatalogSection.Limitations);
        Assert.Equal(RouterOsGeneration.V6,profiles.Generation);
        Assert.Equal("six-profile",Assert.Single(profiles.Rows).Name);
        Assert.Equal("six-limit",Assert.Single(limits.Rows).Name);
        Assert.Equal([
            "/system/resource/print", "/tool/user-manager/profile/print",
            "/system/resource/print", "/tool/user-manager/profile/limitation/print"
        ],spy.Paths);
    }

    [Fact]
    public async Task VersionSeven_UsesNewMenusAndDoesNotRequestPasswords()
    {
        var spy=new Spy("7.21.1 (stable)");
        var api=new RouterGateway(spy);
        var users=await api.ReadUserManagerCatalogAsync(UserManagerCatalogSection.Users);
        Assert.Equal(RouterOsGeneration.V7,users.Generation);
        Assert.Equal("seven-user",Assert.Single(users.Rows).Name);
        Assert.Contains("/user-manager/user/print",spy.Paths);
        Assert.DoesNotContain(spy.Paths,p=>p.StartsWith("/tool/user-manager/",StringComparison.Ordinal));
        Assert.All(spy.Arguments.Where(x=>x is not null),
            attrs=>Assert.DoesNotContain("password",attrs![".proplist"],StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task VersionSeven_ProfilesAndSessionsUseTheirOwnFields()
    {
        var spy=new Spy("7.20rc1");
        var api=new RouterGateway(spy);
        var profile=Assert.Single((await api.ReadUserManagerCatalogAsync(
            UserManagerCatalogSection.Profiles)).Rows);
        var session=Assert.Single((await api.ReadUserManagerCatalogAsync(
            UserManagerCatalogSection.Sessions)).Rows);
        Assert.Equal("first-auth",profile.Extra);
        Assert.Equal("10.0.0.9",session.Address);
        Assert.Contains("/user-manager/session/print",spy.Paths);
    }

    [Fact]
    public async Task UnknownVersion_StopsBeforeReadingAnyUserManagerTable()
    {
        var spy=new Spy("8.0beta");
        await Assert.ThrowsAsync<NotSupportedException>(()=>
            new RouterGateway(spy).ReadUserManagerCatalogAsync(UserManagerCatalogSection.Users));
        Assert.Equal(["/system/resource/print"],spy.Paths);
    }

    [Fact]
    public async Task MissingMenu_IsNotMisreportedAsAnEmptyCollection()
    {
        var spy=new Spy("6.49.19");
        await Assert.ThrowsAsync<RouterOsTrapException>(()=>
            new RouterGateway(spy).ReadUserManagerCatalogAsync(UserManagerCatalogSection.Users));
    }
}
