using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

public sealed class UserManagerSafeDatabaseBackupTests
{
    [Fact]
    public void BackupAllowsOnlyGeneratedNameAndNoOverwrite()
    {
        RouterOsCommandPolicy.Validate("/user-manager/database/save",
            new Dictionary<string,string>
            {
                ["name"]="UM_Najim_20261112_093012_ab12cd",
                ["overwrite"]="no"
            });
        RouterOsCommandPolicy.Validate("/tool/user-manager/database/save",
            new Dictionary<string,string>
            {
                ["name"]="UM_Najim_20261112_093012_ab12cd",
                ["overwrite"]="no"
            });
        Assert.Throws<ArgumentException>(()=>
            RouterOsCommandPolicy.Validate("/user-manager/database/save",
                new Dictionary<string,string>{{"name","../../secrets"},{"overwrite","yes"}}));
        Assert.Throws<ArgumentException>(()=>
            RouterOsCommandPolicy.Validate("/user-manager/database/load",
                new Dictionary<string,string>{{"name","backup"}}));
    }

    [Fact]
    public async Task UnknownRouterVersionDoesNotCreateBackup()
    {
        var mock=new MockRouter();
        await Assert.ThrowsAsync<NotSupportedException>(()=>
            new RouterGateway(mock).CreateUmRouterBackupAsync());
        Assert.DoesNotContain(mock.Commands,x=>x.EndsWith("/save"));
    }

    private sealed class MockRouter : IRouterCommandClient
    {
        public List<string> Commands=[];
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            Commands.Add(command);
            if(command=="/system/resource/print")
                return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>(
                    [new Dictionary<string,string>{{"version","8.0beta"}}]);
            throw new InvalidOperationException(command);
        }
    }
}
