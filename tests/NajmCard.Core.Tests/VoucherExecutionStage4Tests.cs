using System.Net;
using System.Net.Sockets;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

public sealed class VoucherExecutionStage4Tests
{
    private sealed class Clock:IVoucherExecutionClock
    {
        public TimeSpan Elapsed {get;set;}
        public List<TimeSpan> Delays=[];
        public Task DelayAsync(TimeSpan delay,CancellationToken token)
        {token.ThrowIfCancellationRequested();Delays.Add(delay);Elapsed+=delay;return Task.CompletedTask;}
    }
    [Fact]
    public async Task CommandRateIsBoundedWithoutCatchupBursts()
    {
        var clock=new Clock();var pacer=new VoucherCommandPacer(clock);
        var metrics=new VoucherPipelineMetrics();using var scope=new VoucherPipelineOperation(metrics,pacer);
        var starts=new List<TimeSpan>();
        for(int i=0;i<20;i++){await VoucherPipelineOperation.BeforeCommandAsync(default);starts.Add(clock.Elapsed);}
        Assert.All(starts.Zip(starts.Skip(1)),p=>Assert.True(p.Second-p.First>=TimeSpan.FromMilliseconds(50)));
        clock.Elapsed+=TimeSpan.FromSeconds(3);
        await VoucherPipelineOperation.BeforeCommandAsync(default);var afterSlow=clock.Elapsed;
        await VoucherPipelineOperation.BeforeCommandAsync(default);
        Assert.Equal(TimeSpan.FromMilliseconds(50),clock.Elapsed-afterSlow);
        Assert.Equal(22,metrics.Snapshot().RouterRequests);
    }
    [Fact]
    public async Task CancelledQueuedCommandIsNotCountedAsSent()
    {
        var clock=new Clock();using var scope=new VoucherPipelineOperation(pacer:new VoucherCommandPacer(clock));
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>VoucherPipelineOperation.BeforeCommandAsync(cancelled.Token));
        Assert.Equal(0,scope.Metrics.Snapshot().RouterRequests);
    }
    [Fact]
    public async Task AReadyFirstReadDoesNotWaitOrRetry()
    {
        var clock=new Clock();int calls=0;
        var result=await VoucherReadbackPoller.VerifyAsync(_=>{calls++;return Task.FromResult(new VoucherReadbackEvidence(true,false));},clock:clock);
        Assert.True(result.Confirmed);Assert.Equal(1,calls);Assert.Empty(clock.Delays);
    }
    [Fact]
    public async Task SlowReadsConsumePollingIntervalInsteadOfAddingAFixedDelay()
    {
        var clock=new Clock();int calls=0;
        var result=await VoucherReadbackPoller.VerifyAsync(_=>
        {calls++;clock.Elapsed+=TimeSpan.FromMilliseconds(300);return Task.FromResult(new VoucherReadbackEvidence(calls==2,true));},clock:clock);
        Assert.True(result.Confirmed);Assert.Equal(2,calls);Assert.Empty(clock.Delays);
    }
    [Fact]
    public async Task PendingVerificationHasAFiniteBudgetAndNeverClaimsSuccess()
    {
        var clock=new Clock();int calls=0;using var operation=new VoucherPipelineOperation();
        var result=await VoucherReadbackPoller.VerifyAsync(_=>{calls++;return Task.FromResult(new VoucherReadbackEvidence(false,true));},clock:clock);
        Assert.False(result.Confirmed);Assert.Equal(5,calls);
        Assert.Equal(TimeSpan.FromMilliseconds(750),clock.Elapsed);Assert.Equal(4,operation.Metrics.Snapshot().VerificationRetries);
    }
    [Fact]
    public async Task InvalidEvidenceDoesNotPollAgain()
    {
        int reads=0;var clock=new Clock();
        await VoucherReadbackPoller.VerifyAsync(_=>{reads++;return Task.FromResult(new VoucherReadbackEvidence(false,false));},clock:clock);
        Assert.Equal(1,reads);Assert.Empty(clock.Delays);
    }
    [Fact]
    public async Task UserManagerV6RecoveryUsesTheUsernameFilterOnTheWire()
    {
        using var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var server=Task.Run(async()=>
        {
            using var peer=await listener.AcceptTcpClientAsync(limit.Token);using var stream=peer.GetStream();
            await RouterOsProtocol.ReadSentenceAsync(stream,limit.Token);
            await RouterOsProtocol.WriteSentenceAsync(stream,["!done"],limit.Token);
            var words=await RouterOsProtocol.ReadSentenceAsync(stream,limit.Token);
            Assert.Equal(new[]{"/tool/user-manager/user/print","?username=card-1"},words);
            await RouterOsProtocol.WriteSentenceAsync(stream,["!done"],limit.Token);
        },limit.Token);
        await using var client=await RouterOsClient.ConnectAsync(new RouterEndpoint("127.0.0.1",
            ((IPEndPoint)listener.LocalEndpoint).Port,"test","test"),limit.Token);
        Assert.Empty(await client.QueryByNameAsync("/tool/user-manager/user/print","card-1",cancellationToken:limit.Token));
        await server;
    }
}
