using System.Net;
using System.Net.Sockets;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>
/// Real loopback RouterOS API sentences. Tagged commands are pipelined,
/// individually attributed and drained even when !trap precedes !done.
/// </summary>
public sealed class RouterOsTaggedWindowTcpTests
{
    private static RouterTaggedAddCommand Add(string username)=>
        new("/ip/hotspot/user/add",new Dictionary<string,string>
        {
            ["name"]=username,["profile"]="gold",
            ["comment"]="NajmCard batch demo"
        });

    [Fact]
    public async Task OutOfOrderTaggedReplies_AreMappedToOriginalVoucherIndices()
    {
        using var listener=new TcpListener(IPAddress.Loopback,0);
        listener.Start();
        int port=((IPEndPoint)listener.LocalEndpoint).Port;
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var server=Task.Run(async()=>
        {
            using var remote=await listener.AcceptTcpClientAsync(deadline.Token);
            using var stream=remote.GetStream();
            Assert.Equal("/login",
                (await RouterOsProtocol.ReadSentenceAsync(stream,deadline.Token))[0]);
            await RouterOsProtocol.WriteSentenceAsync(stream,["!done"],deadline.Token);
            var names=new string[3];
            for(int i=0;i<3;i++)
            {
                var sentence=await RouterOsProtocol.ReadSentenceAsync(stream,deadline.Token);
                Assert.Equal("/ip/hotspot/user/add",sentence[0]);
                Assert.Contains(".tag="+i,sentence);
                names[i]=Assert.Single(sentence.Where(w=>w.StartsWith("=name=")))[6..];
            }
            Assert.Equal(["111001","111002","111003"],names);
            // Deliberately reply in reverse order; the middle add is rejected.
            await RouterOsProtocol.WriteSentenceAsync(stream,
                ["!done",".tag=2","=ret=*C"],deadline.Token);
            await RouterOsProtocol.WriteSentenceAsync(stream,
                ["!trap",".tag=1","=message=profile rejected"],deadline.Token);
            await RouterOsProtocol.WriteSentenceAsync(stream,
                ["!done",".tag=0","=ret=*A"],deadline.Token);
            await RouterOsProtocol.WriteSentenceAsync(stream,
                ["!done",".tag=1"],deadline.Token);
            // Ensure the completed response window does not leak a stale !done
            // into the NEXT ordinary command on the same TCP stream.
            Assert.Equal("/ip/hotspot/user/print",
                (await RouterOsProtocol.ReadSentenceAsync(stream,deadline.Token))[0]);
            await RouterOsProtocol.WriteSentenceAsync(stream,
                ["!re","=name=still-alive"],deadline.Token);
            await RouterOsProtocol.WriteSentenceAsync(stream,["!done"],deadline.Token);
        },deadline.Token);

        await using(var client=await RouterOsClient.ConnectAsync(
            new RouterEndpoint("127.0.0.1",port,"admin","secret"),deadline.Token))
        {
            var results=await client.AddWindowAsync(
                [Add("111001"),Add("111002"),Add("111003")],deadline.Token);
            Assert.Equal(3,results.Count);
            Assert.False(results[0].Rejected);
            Assert.Equal("*A",results[0].ReturnId);
            Assert.True(results[1].Rejected);
            Assert.Contains("profile rejected",results[1].Error);
            Assert.False(results[2].Rejected);
            Assert.Equal("*C",results[2].ReturnId);
            var read=await client.CommandAsync("/ip/hotspot/user/print",
                cancellationToken:deadline.Token);
            Assert.Equal("still-alive",Assert.Single(read)["name"]);
        }
        await server;
    }

    [Fact]
    public async Task UntaggedReply_ClosesUnsafeTransportInsteadOfGuessingWhichCardSucceeded()
    {
        using var listener=new TcpListener(IPAddress.Loopback,0);
        listener.Start();
        int port=((IPEndPoint)listener.LocalEndpoint).Port;
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var server=Task.Run(async()=>
        {
            using var remote=await listener.AcceptTcpClientAsync(deadline.Token);
            using var stream=remote.GetStream();
            await RouterOsProtocol.ReadSentenceAsync(stream,deadline.Token);
            await RouterOsProtocol.WriteSentenceAsync(stream,["!done"],deadline.Token);
            await RouterOsProtocol.ReadSentenceAsync(stream,deadline.Token);
            await RouterOsProtocol.ReadSentenceAsync(stream,deadline.Token);
            await RouterOsProtocol.WriteSentenceAsync(stream,
                ["!done","=ret=*A"],deadline.Token); // no .tag: unknowable
        },deadline.Token);
        await using(var client=await RouterOsClient.ConnectAsync(
            new RouterEndpoint("127.0.0.1",port,"admin","secret"),deadline.Token))
        {
            await Assert.ThrowsAsync<InvalidDataException>(()=>
                client.AddWindowAsync([Add("301"),Add("302")],deadline.Token));
            Assert.False(client.IsUsable);
            await Assert.ThrowsAsync<ObjectDisposedException>(()=>
                client.CommandAsync("/ip/hotspot/user/print",
                    cancellationToken:deadline.Token));
        }
        await server;
    }

    [Fact]
    public async Task TaggedWindow_RejectsNonAccountWritesBeforeOpeningTcpStream()
    {
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        // No router required: validation occurs before any I/O.
        using var listener=new TcpListener(IPAddress.Loopback,0);
        listener.Start();
        int port=((IPEndPoint)listener.LocalEndpoint).Port;
        var server=Task.Run(async()=>
        {
            using var remote=await listener.AcceptTcpClientAsync(deadline.Token);
            using var stream=remote.GetStream();
            await RouterOsProtocol.ReadSentenceAsync(stream,deadline.Token);
            await RouterOsProtocol.WriteSentenceAsync(stream,["!done"],deadline.Token);
        });
        await using(var client=await RouterOsClient.ConnectAsync(
            new RouterEndpoint("127.0.0.1",port,"admin","secret"),deadline.Token))
        {
            await Assert.ThrowsAnyAsync<ArgumentException>(()=>
                client.AddWindowAsync(
                    [Add("1"),new RouterTaggedAddCommand("/system/reboot",
                        new Dictionary<string,string>())],deadline.Token));
            Assert.True(client.IsUsable);
        }
        await server;
    }
}
