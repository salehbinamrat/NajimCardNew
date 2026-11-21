using System.Net;
using System.Net.Sockets;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

public sealed class RouterReadTransportStage3Tests
{
    [Theory]
    [InlineData("?.id=*A")]
    [InlineData("?name=card")]
    [InlineData("?username=card")]
    [InlineData("=count-only=")]
    public void OneRecordAndCountRequestsHaveShortDeadlines(string word)
    {
        var policy=RouterOsCommandTimeoutPolicy.ForRequest(["/tool/user-manager/user/print",word]);
        Assert.Equal(TimeSpan.FromSeconds(20),policy.Total);
        Assert.Equal(TimeSpan.FromSeconds(20),policy.Idle);
        Assert.Equal(TimeSpan.FromMinutes(10),RouterOsCommandTimeoutPolicy.ForRequest(
            ["/tool/user-manager/user/print","=.proplist=.id,username"]).Total);
        Assert.Equal(TimeSpan.FromMinutes(10),RouterOsCommandTimeoutPolicy.ForRequest(
            ["/tool/user-manager/session/print","?name=card","?user=card","?#|"]).Total);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CorruptUtf8ReconnectsReadsButNeverReplaysWrites(bool write)
    {
        using var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var endpoint=new RouterEndpoint("127.0.0.1",((IPEndPoint)listener.LocalEndpoint).Port,"test","test");
        var commands=new List<string>();
        var server=Task.Run(async()=>
        {
            for(var attempt=0;attempt<2;attempt++)
            {
                using var peer=await listener.AcceptTcpClientAsync(deadline.Token);using var stream=peer.GetStream();
                await RouterOsProtocol.ReadSentenceAsync(stream,deadline.Token);
                await RouterOsProtocol.WriteSentenceAsync(stream,["!done"],deadline.Token);
                commands.Add((await RouterOsProtocol.ReadSentenceAsync(stream,deadline.Token))[0]);
                if(attempt==0)
                    await stream.WriteAsync(new byte[]{3,33,114,101,4,61,120,61,255,0,5,33,100,111,110,101,0},deadline.Token);
                else
                {
                    await RouterOsProtocol.WriteSentenceAsync(stream,["!re","=version=6.49.19"],deadline.Token);
                    await RouterOsProtocol.WriteSentenceAsync(stream,["!done"],deadline.Token);
                }
            }
        },deadline.Token);
        int reconnects=0;
        await using var session=new ResilientRouterSession(await RouterOsClient.ConnectAsync(endpoint,deadline.Token),async ct=>
        {reconnects++;return await RouterOsClient.ConnectAsync(endpoint,ct);});
        if(write)
        {
            await Assert.ThrowsAsync<InvalidDataException>(()=>session.CommandAsync("/ip/hotspot/user/set",
                new Dictionary<string,string>{{".id","*A"},{"disabled","yes"}},deadline.Token));
            Assert.Equal(0,reconnects);
        }
        var records=await session.CommandAsync("/system/resource/print",cancellationToken:deadline.Token);
        Assert.Equal("6.49.19",Assert.Single(records)["version"]);
        await server;Assert.Equal(1,reconnects);Assert.Equal(2,commands.Count);
        Assert.Equal(write?1:0,commands.Count(c=>c.EndsWith("/set")));
    }

    [Fact]
    public async Task SessionQueryUsesOneSafeOrExpressionAndExactIdSupportsSessionMenu()
    {
        using var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var server=Task.Run(async()=>
        {
            using var peer=await listener.AcceptTcpClientAsync(deadline.Token);using var stream=peer.GetStream();
            await RouterOsProtocol.ReadSentenceAsync(stream,deadline.Token);
            await RouterOsProtocol.WriteSentenceAsync(stream,["!done"],deadline.Token);
            var words=await RouterOsProtocol.ReadSentenceAsync(stream,deadline.Token);
            Assert.Equal("/tool/user-manager/session/print",words[0]);
            foreach(var field in new[]{"user","username","user-name","name"})
            {Assert.Contains("?"+field+"=card",words);Assert.Contains("?"+field+"=*F",words);}
            Assert.Equal(7,words.Count(w=>w=="?#|"));
            await RouterOsProtocol.WriteSentenceAsync(stream,["!done"],deadline.Token);
            words=await RouterOsProtocol.ReadSentenceAsync(stream,deadline.Token);
            Assert.Equal(new[]{"/tool/user-manager/session/print","?.id=*A"},words);
            await RouterOsProtocol.WriteSentenceAsync(stream,["!done"],deadline.Token);
        },deadline.Token);
        await using var client=await RouterOsClient.ConnectAsync(new RouterEndpoint("127.0.0.1",
            ((IPEndPoint)listener.LocalEndpoint).Port,"test","test"),deadline.Token);
        await Assert.ThrowsAsync<ArgumentException>(()=>client.QuerySessionsForAccountAsync(
            "/tool/user-manager/user/print","card",cancellationToken:deadline.Token));
        await Assert.ThrowsAsync<ArgumentException>(()=>client.QuerySessionsForAccountAsync(
            "/tool/user-manager/session/print","card","*A,*B",cancellationToken:deadline.Token));
        Assert.Empty(await client.QuerySessionsForAccountAsync("/tool/user-manager/session/print","card","*F",
            cancellationToken:deadline.Token));
        Assert.Empty(await client.QueryByIdAsync("/tool/user-manager/session/print","*A",cancellationToken:deadline.Token));
        await server;
    }

    [Fact]
    public async Task CancellationOfSlowPartialReplyDiscardsConnectionWithoutRetry()
    {
        using var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var sent=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server=Task.Run(async()=>
        {
            using var peer=await listener.AcceptTcpClientAsync(deadline.Token);using var stream=peer.GetStream();
            await RouterOsProtocol.ReadSentenceAsync(stream,deadline.Token);
            await RouterOsProtocol.WriteSentenceAsync(stream,["!done"],deadline.Token);
            await RouterOsProtocol.ReadSentenceAsync(stream,deadline.Token);
            await stream.WriteAsync(new byte[]{20,61,120},deadline.Token);sent.SetResult();
            await release.Task.WaitAsync(deadline.Token);
        },deadline.Token);
        var endpoint=new RouterEndpoint("127.0.0.1",((IPEndPoint)listener.LocalEndpoint).Port,"test","test");
        int reconnects=0;
        await using var session=new ResilientRouterSession(await RouterOsClient.ConnectAsync(endpoint,deadline.Token),_=>
        {reconnects++;throw new InvalidOperationException("No automatic retry after caller cancellation");});
        using var cancel=new CancellationTokenSource();
        var read=session.QueryByIdAsync("/tool/user-manager/user/print","*A",cancellationToken:cancel.Token);
        await sent.Task.WaitAsync(deadline.Token);cancel.Cancel();
        try{await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>read);Assert.Equal(0,reconnects);}
        finally{release.SetResult();await server;}
    }
}
