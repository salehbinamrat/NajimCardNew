using System.Text;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

public sealed class RouterOsProtocolTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(127, 1)]
    [InlineData(128, 2)]
    [InlineData(16383, 2)]
    [InlineData(16384, 3)]
    [InlineData(2097151, 3)]
    [InlineData(2097152, 4)]
    [InlineData(268435455, 4)]
    [InlineData(268435456, 5)]
    public async Task LengthEncodingRoundtrips(uint value, int count)
    {
        byte[] encoded = RouterOsProtocol.EncodeLength(value);
        Assert.Equal(count, encoded.Length);
        using var bytes = new MemoryStream(encoded);
        Assert.Equal(value, await RouterOsProtocol.ReadLengthAsync(bytes, default));
    }

    [Fact]
    public async Task SentencesRoundtripUtf8IncludingArabic()
    {
        string[] values = ["/ip/hotspot/user/add", "=name=1234", "=comment=نجم كارد", "=password=abcDEF111"];
        using var stream = new MemoryStream();
        await RouterOsProtocol.WriteSentenceAsync(stream, values, default);
        stream.Position = 0;
        Assert.Equal(values, await RouterOsProtocol.ReadSentenceAsync(stream, default));
    }

    // Every RouterOS command should be framed and sent as one transport
    // operation. Many tiny writes force extra TCP packets/ACK waits.
    private sealed class CountingStream : Stream
    {
        private readonly MemoryStream _inner=new();
        public int Writes {get;private set;}
        public byte[] Bytes => _inner.ToArray();
        public override bool CanRead=>false;
        public override bool CanSeek=>false;
        public override bool CanWrite=>true;
        public override long Length=>_inner.Length;
        public override long Position {get=>_inner.Position;set=>throw new NotSupportedException();}
        public override void Flush()=>_inner.Flush();
        public override Task FlushAsync(CancellationToken ct)=>Task.CompletedTask;
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,
            CancellationToken ct=default)
        {
            Writes++;
            return _inner.WriteAsync(buffer,ct);
        }
        public override void Write(byte[] buffer,int offset,int count)
        {
            Writes++;
            _inner.Write(buffer,offset,count);
        }
        public override int Read(byte[] buffer,int offset,int count)=>
            throw new NotSupportedException();
        public override long Seek(long offset,SeekOrigin origin)=>
            throw new NotSupportedException();
        public override void SetLength(long value)=>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task LargeCommand_HasSingleNetworkWrite_AndRoundtripsExactly()
    {
        var words=new[] {"/tool/user-manager/user/add",
            "=username=12345678","=password=Secret9876","=customer=admin",
            "=comment=شبكة نجم كارد".PadRight(512,'x')};
        using var transport=new CountingStream();
        await RouterOsProtocol.WriteSentenceAsync(transport,words,default);
        Assert.Equal(1,transport.Writes);
        using var replay=new MemoryStream(transport.Bytes);
        Assert.Equal(words,await RouterOsProtocol.ReadSentenceAsync(replay,default));
    }

    [Fact]
    public async Task TruncatedSentenceThrowsInsteadOfHanging()
    {
        using var stream = new MemoryStream(new byte[] { 4, (byte)'A', (byte)'B' });
        await Assert.ThrowsAsync<EndOfStreamException>(async () => await RouterOsProtocol.ReadSentenceAsync(stream, default));
    }

    [Fact]
    public async Task ExcessiveWordIsRejectedBeforeAllocating()
    {
        using var stream = new MemoryStream(RouterOsProtocol.EncodeLength(9_000_000));
        await Assert.ThrowsAsync<InvalidDataException>(async () => await RouterOsProtocol.ReadSentenceAsync(stream, default));
    }
}
