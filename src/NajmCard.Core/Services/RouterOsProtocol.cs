using System.Buffers.Binary;
using System.Text;

namespace NajmCard.Core.Services;

/// <summary>MikroTik RouterOS API binary word framing; independent of transport.</summary>
public static class RouterOsProtocol
{
    private const uint MaxWord = 0xFFFFFFFF;
    // RouterOS catalog scans can contain tens of thousands of UTF-8 words.
    // Reuse one strict decoder rather than allocating an encoding per field.
    private static readonly UTF8Encoding StrictUtf8=new(false,true);
    public static byte[] EncodeLength(uint value)
    {
        if (value < 0x80) return [(byte)value];
        if (value < 0x4000) return [(byte)((value >> 8) | 0x80), (byte)value];
        if (value < 0x200000) return [(byte)((value >> 16) | 0xC0), (byte)(value >> 8), (byte)value];
        if (value < 0x10000000)
            return [(byte)((value >> 24) | 0xE0), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
        return [0xF0, (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
    }

    public static async Task<uint> ReadLengthAsync(Stream stream, CancellationToken token)
    {
        byte first = (await ReadExactAsync(stream, 1, token))[0];
        if ((first & 0x80) == 0) return first;
        if ((first & 0xC0) == 0x80)
        {
            byte[] b = await ReadExactAsync(stream, 1, token);
            return (uint)((first & 0x3F) << 8 | b[0]);
        }
        if ((first & 0xE0) == 0xC0)
        {
            byte[] b = await ReadExactAsync(stream, 2, token);
            return (uint)((first & 0x1F) << 16 | b[0] << 8 | b[1]);
        }
        if ((first & 0xF0) == 0xE0)
        {
            byte[] b = await ReadExactAsync(stream, 3, token);
            return (uint)((first & 0x0F) << 24 | b[0] << 16 | b[1] << 8 | b[2]);
        }
        if (first != 0xF0) throw new InvalidDataException("Unsupported RouterOS word length prefix");
        byte[] tail = await ReadExactAsync(stream, 4, token);
        return BinaryPrimitives.ReadUInt32BigEndian(tail);
    }

    public static async Task WriteSentenceAsync(Stream stream, IReadOnlyList<string> words, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(words);
        token.ThrowIfCancellationRequested();
        // RouterOS API is length-framed. Buffer the complete command, then
        // perform ONE asynchronous network write instead of two per word.
        // This avoids the small-write/ACK latency that compounds for every
        // add + activate + exact-ID verification during voucher issuance.
        using var frame=new MemoryStream(256);
        foreach(var word in words)
        {
            if(word is null)throw new ArgumentException("Null RouterOS API word.");
            var bytes=Encoding.UTF8.GetBytes(word);
            frame.Write(EncodeLength((uint)bytes.Length));
            frame.Write(bytes);
        }
        frame.WriteByte(0);
        await stream.WriteAsync(frame.GetBuffer().AsMemory(0,checked((int)frame.Length)),token);
        await stream.FlushAsync(token);
    }

    public static async Task<IReadOnlyList<string>> ReadSentenceAsync(Stream stream, CancellationToken token)
    {
        var words = new List<string>();
        // A typical RouterOS reply contains many tiny length prefixes. Reuse
        // one scratch buffer per sentence rather than allocating 1-4 byte
        // arrays for every word and every prefix continuation.
        var prefix = new byte[5];
        while (true)
        {
            uint length = await ReadLengthBufferedAsync(stream, prefix, token);
            if (length == 0) return words;
            if (length > 8 * 1024 * 1024) throw new InvalidDataException("Oversized RouterOS API response");
            var bytes = await ReadExactAsync(stream, (int)length, token);
            words.Add(StrictUtf8.GetString(bytes));
            if (words.Count > 5000) throw new InvalidDataException("Excessive RouterOS API words");
        }
    }

    private static async Task<uint> ReadLengthBufferedAsync(
        Stream stream, byte[] prefix, CancellationToken token)
    {
        await ReadExactIntoAsync(stream, prefix.AsMemory(0,1), token);
        byte first=prefix[0];
        if ((first & 0x80)==0) return first;
        int extra=(first & 0xC0)==0x80 ? 1 :
            (first & 0xE0)==0xC0 ? 2 :
            (first & 0xF0)==0xE0 ? 3 :
            first==0xF0 ? 4 :
            throw new InvalidDataException("Unsupported RouterOS word length prefix");
        await ReadExactIntoAsync(stream,prefix.AsMemory(1,extra),token);
        return extra switch
        {
            1 => (uint)((first & 0x3F)<<8 | prefix[1]),
            2 => (uint)((first & 0x1F)<<16 | prefix[1]<<8 | prefix[2]),
            3 => (uint)((first & 0x0F)<<24 | prefix[1]<<16 | prefix[2]<<8 | prefix[3]),
            _ => BinaryPrimitives.ReadUInt32BigEndian(prefix.AsSpan(1,4))
        };
    }

    private static async Task ReadExactIntoAsync(
        Stream stream, Memory<byte> buffer, CancellationToken token)
    {
        while (!buffer.IsEmpty)
        {
            int count=await stream.ReadAsync(buffer,token);
            if (count==0) throw new EndOfStreamException("RouterOS disconnected unexpectedly");
            buffer=buffer[count..];
        }
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int count, CancellationToken token)
    {
        var buf = new byte[count];
        int cursor = 0;
        while (cursor < count)
        {
            int bytes = await stream.ReadAsync(buf.AsMemory(cursor), token);
            if (bytes == 0) throw new EndOfStreamException("RouterOS disconnected unexpectedly");
            cursor += bytes;
        }
        return buf;
    }
}
