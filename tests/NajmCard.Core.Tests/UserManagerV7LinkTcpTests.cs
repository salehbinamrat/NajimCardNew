using System.Net;
using System.Net.Sockets;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>
/// Real RouterOS API TCP framing test, including the exact-ID readback after
/// an acknowledged v7 profile-limitation link. No real router is changed.
/// </summary>
public sealed class UserManagerV7LinkTcpTests
{
    [Fact]
    public async Task LinkIsVerifiedByExactIdWithoutRetryingWrite()
    {
        using var listener=new TcpListener(IPAddress.Loopback,0);
        listener.Start();
        int port=((IPEndPoint)listener.LocalEndpoint).Port;
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        int additions=0;

        var server=Task.Run(async ()=>
        {
            using var remote=await listener.AcceptTcpClientAsync(deadline.Token);
            using var stream=remote.GetStream();

            var login=await RouterOsProtocol.ReadSentenceAsync(stream,deadline.Token);
            Assert.Equal("/login",login[0]);
            await RouterOsProtocol.WriteSentenceAsync(stream,["!done"],deadline.Token);

            var version=await RouterOsProtocol.ReadSentenceAsync(stream,deadline.Token);
            Assert.Equal("/system/resource/print",version[0]);
            await RouterOsProtocol.WriteSentenceAsync(stream,
                ["!re","=version=7.21"],deadline.Token);
            await RouterOsProtocol.WriteSentenceAsync(stream,["!done"],deadline.Token);

            var profiles=await RouterOsProtocol.ReadSentenceAsync(stream,deadline.Token);
            Assert.Equal("/user-manager/profile/print",profiles[0]);
            await RouterOsProtocol.WriteSentenceAsync(stream,
                ["!re","=name=monthly"],deadline.Token);
            await RouterOsProtocol.WriteSentenceAsync(stream,["!done"],deadline.Token);

            var limits=await RouterOsProtocol.ReadSentenceAsync(stream,deadline.Token);
            Assert.Equal("/user-manager/limitation/print",limits[0]);
            await RouterOsProtocol.WriteSentenceAsync(stream,
                ["!re","=name=quota"],deadline.Token);
            await RouterOsProtocol.WriteSentenceAsync(stream,["!done"],deadline.Token);

            var existing=await RouterOsProtocol.ReadSentenceAsync(stream,deadline.Token);
            Assert.Equal("/user-manager/profile-limitation/print",existing[0]);
            await RouterOsProtocol.WriteSentenceAsync(stream,["!done"],deadline.Token);

            var write=await RouterOsProtocol.ReadSentenceAsync(stream,deadline.Token);
            Assert.Equal("/user-manager/profile-limitation/add",write[0]);
            Assert.Contains("=profile=monthly",write);
            Assert.Contains("=limitation=quota",write);
            additions++;
            await RouterOsProtocol.WriteSentenceAsync(stream,
                ["!done","=ret=*A"],deadline.Token);

            var verify=await RouterOsProtocol.ReadSentenceAsync(stream,deadline.Token);
            Assert.Equal("/user-manager/profile-limitation/print",verify[0]);
            Assert.Contains("?.id=*A",verify);
            Assert.Contains("=.proplist=.id,profile,limitation",verify);
            Assert.Single(verify.Where(x=>x.StartsWith("?.id=",StringComparison.Ordinal)));
            await RouterOsProtocol.WriteSentenceAsync(stream,
                ["!re","=.id=*A","=profile=monthly","=limitation=quota"],deadline.Token);
            await RouterOsProtocol.WriteSentenceAsync(stream,["!done"],deadline.Token);
        },deadline.Token);

        await using(var client=await RouterOsClient.ConnectAsync(
            new RouterEndpoint("127.0.0.1",port,"admin","secret"),deadline.Token))
        {
            var result=await new RouterGateway(client)
                .LinkUmProfileLimitationAsync("monthly","quota",deadline.Token);
            Assert.True(result.Success);
            Assert.False(result.Uncertain);
        }
        await server;
        Assert.Equal(1,additions);
    }
}
