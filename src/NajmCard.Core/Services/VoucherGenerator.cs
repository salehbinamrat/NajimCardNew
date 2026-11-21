using System.Numerics;
using System.Security.Cryptography;
using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>Cryptographic sampling without replacement from the unoccupied number space.</summary>
public static class VoucherGenerator
{
    public const string Digits = "0123456789";
    public const string UppercaseLetters = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    public const string Mixed = Digits + UppercaseLetters;
    public const string LowercaseLetters = "abcdefghijklmnopqrstuvwxyz";
    public const string LowercaseMixed = Digits + LowercaseLetters;

    // One validation path for both live artwork samples and actual voucher batches.
    // Preview intentionally returns plain text, never an issuable Voucher entity.
    private sealed record PreparedGeneration(
        string Prefix, string Characters, int SuffixLength,
        string PasswordCharacters, int PasswordLength);

    private static PreparedGeneration Prepare(VoucherGenerationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if(options.Count is < 1 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(options.Count));
        if(options.Length is < 4 or > 32)
            throw new ArgumentOutOfRangeException(nameof(options.Length));
        var prefix=options.Prefix ?? "";
        if(prefix.Length > 24 || prefix.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '-')))
            throw new ArgumentException("بادئة الكرت يجب أن تتكون من أحرف إنجليزية وأرقام أو - و _.");
        if(string.IsNullOrWhiteSpace(options.Profile))
            throw new ArgumentException("اختر الباقة أولاً.");
        // SQLite and router collision checks treat usernames without case.
// Count a/A as ONE choice, otherwise the advertised space is inflated.
        var chars=Alphabet(options.Characters,"أحرف اسم المستخدم",minimum:2,ignoreCase:true);
        var suffixLength=options.LengthIncludesPrefix ? options.Length-prefix.Length : options.Length;
        if(suffixLength < 0)
            throw new ArgumentException("بادئة الكرت أطول من طول الرقم المطلوب.");
        if(options.LengthIncludesPrefix && suffixLength == 0 && options.Count != 1)
            throw new ArgumentException("البادئة تشغل رقم الكرت كاملا؛ لا يمكن إنشاء عدة كروت مختلفة.");
        var passwordLength=options.PasswordLength==0 && options.PasswordCharacters is null
            ? options.Length : options.PasswordLength;
        // Hadramout's code_only mode carries an empty password, not the
        // username as a synthetic hidden password. These are three distinct
        // modes: username-only, same password, and a separate password.
        var passwordChars=options.UsernameOnly || options.PasswordCharacters is null
            ? chars : Alphabet(options.PasswordCharacters,"أحرف كلمة المرور",minimum:1);
        if(!options.UsernameOnly && !options.PasswordEqualsUsername &&
            passwordLength is < 1 or > 64)
            throw new ArgumentException("طول كلمة المرور يجب أن يكون من 1 إلى 64.");
        if(BigInteger.Pow(chars.Length,suffixLength)<options.Count)
            throw new ArgumentException(
                "مساحة أرقام الكروت لا تكفي للعدد المطلوب؛ زد الطول أو مجموعة الأحرف.");
        return new PreparedGeneration(prefix,chars,suffixLength,passwordChars,passwordLength);
    }

    /// <summary>
    /// Create an ephemeral preview of the precise generation configuration.
    /// No database, RouterOS connection, batch identifier or Voucher record is created.
    /// </summary>
    public static (string Username, string Password) PreviewSample(
        VoucherGenerationOptions options)
    {
        var p=Prepare(options);
        using var entropy=new SecureTokenSampler();
        var username=p.Prefix+entropy.NextToken(p.Characters,p.SuffixLength);
        var password=options.UsernameOnly ? "" :
            options.PasswordEqualsUsername ? username :
            DifferentPassword(p.PasswordCharacters,p.PasswordLength,username,entropy);
        return (username,password);
    }

    public static IReadOnlyList<Voucher> Generate(VoucherGenerationOptions options,
        IReadOnlySet<string>? reserved = null)
    {
        using var measurement=VoucherPipelineOperation.Measure(VoucherPipelineStage.Generation);
        var p=Prepare(options);
        var used=new HashSet<string>(
            reserved ?? new HashSet<string>(),StringComparer.OrdinalIgnoreCase);
        // Reject an exhausted local/router namespace before spending thousands
        // of cryptographic RNG calls retrying inevitable collisions.
        var alphabet=new HashSet<char>(p.Characters.Select(char.ToUpperInvariant));
        var ordinals=p.Characters.Select((c,i)=>(c:char.ToUpperInvariant(c),i)).ToDictionary(x=>x.c,x=>x.i);
        var blocked=used.Where(name=>IsCandidateName(name,p.Prefix,p.SuffixLength,alphabet))
            .Select(name=>Ordinal(name.AsSpan(p.Prefix.Length),ordinals)).Order().ToArray();
        var remaining=BigInteger.Pow(p.Characters.Length,p.SuffixLength)-blocked.Length;
        if(remaining<options.Count)
            throw new ArgumentException(
                "مساحة أرقام الكروت المتبقية لا تكفي للدفعة؛ غيّر البادئة أو الطول أو مجموعة الأحرف.");
        var results=new List<Voucher>(options.Count);
        using var entropy=new SecureTokenSampler();
        // A batch is created at one consistent time, without querying the
        // system clock for every randomly generated voucher.
        var batchCreatedAt=DateTimeOffset.UtcNow;
        string batch=batchCreatedAt.ToString("yyyyMMddHHmmss")+"-"+
            Guid.NewGuid().ToString("N")[..8];
        // Sparse Fisher-Yates over FREE ranks. Mapping a rank through sorted
        // occupied ordinals is logarithmic; even a single free number is found
        // without scanning an enormous space or relying on lucky random retries.
        var swaps=new Dictionary<BigInteger,BigInteger>();
        while(results.Count<options.Count)
        {
            var draw=entropy.NextBelow(remaining);
            var rank=swaps.GetValueOrDefault(draw,draw);
            var last=--remaining;
            swaps[draw]=swaps.GetValueOrDefault(last,last);swaps.Remove(last);
            int lo=0,hi=blocked.Length;
            while(lo<hi){int mid=lo+(hi-lo)/2;if(blocked[mid]-mid<=rank)lo=mid+1;else hi=mid;}
            string username=p.Prefix+Token(rank+lo,p.Characters,p.SuffixLength);
            string password=options.UsernameOnly ? "" :
                options.PasswordEqualsUsername ? username :
                DifferentPassword(p.PasswordCharacters,p.PasswordLength,username,entropy);
            results.Add(new Voucher(username,password,options.Profile,batch,batchCreatedAt));
        }
        return results;
    }

    private static BigInteger Ordinal(ReadOnlySpan<char> token,IReadOnlyDictionary<char,int> ordinals)
    {
        BigInteger value=0;
        foreach(char c in token)value=value*ordinals.Count+ordinals[char.ToUpperInvariant(c)];
        return value;
    }
    private static string Token(BigInteger value,string chars,int length)
    {
        Span<char> token=stackalloc char[length];
        for(int i=length-1;i>=0;i--){value=BigInteger.DivRem(value,chars.Length,out var digit);token[i]=chars[(int)digit];}
        return new string(token);
    }

    private static bool IsCandidateName(string name,string prefix,int suffixLength,
        IReadOnlySet<char> alphabet)
    {
        if(name is null || name.Length!=prefix.Length+suffixLength ||
           !name.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))
            return false;
        for(var i=prefix.Length;i<name.Length;i++)
            if(!alphabet.Contains(char.ToUpperInvariant(name[i]))||
                !char.IsAscii(name[i])&&!string.Equals(name[i].ToString(),char.ToUpperInvariant(name[i]).ToString(),StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }

    private static string Alphabet(string? input,string label,int minimum,
        bool ignoreCase=false)
    {
        // Passwords retain case-sensitive letters; username tokens must use
        // the case-insensitive SQLite uniqueness and RouterOS preflight key.
        string chars=new((input??"")
            .DistinctBy(c=>ignoreCase?char.ToUpperInvariant(c):c).ToArray());
        if(chars.Length<minimum || chars.Any(c=>!char.IsAsciiLetterOrDigit(c)))
            throw new ArgumentException(label+" يجب أن تكون أحرفا إنجليزية أو أرقاما فقط.");
        return chars;
    }

    private static string DifferentPassword(
        string chars,int length,string username,SecureTokenSampler entropy)
    {
        for(var attempt=0;attempt<16;attempt++)
        {
            var password=entropy.NextToken(chars,length);
            if(!string.Equals(password,username,StringComparison.Ordinal))
                return password;
        }
        throw new ArgumentException(
            "إعدادات كلمة المرور تكرر اسم المستخدم ولا تسمح بإنشاء كلمة مختلفة.");
    }

    /// <summary>
    /// Batch-local cryptographic random byte stream. A separate platform RNG
    /// request per character is expensive for thousands of voucher numbers
    /// and passwords. Fill a bounded block at once, then use rejection
    /// sampling so every alphabet character stays exactly equiprobable.
    /// There is no shared global random state, predictable seed or modulo bias.
    /// </summary>
    private sealed class SecureTokenSampler : IDisposable
    {
        private readonly byte[] _buffer=new byte[4096];
        private int _position=4096;

        public BigInteger NextBelow(BigInteger bound)
        {
            if(bound<=1)return BigInteger.Zero;
            int bits=(int)(bound-1).GetBitLength();
            Span<byte> bytes=stackalloc byte[(bits+7)/8];
            BigInteger value;
            do
            {
                for(int i=0;i<bytes.Length;i++)
                {
                    if(_position==_buffer.Length){RandomNumberGenerator.Fill(_buffer);_position=0;}
                    bytes[i]=_buffer[_position++];
                }
                bytes[^1]&=(byte)(0xff>>(bytes.Length*8-bits));
                value=new BigInteger(bytes,isUnsigned:true,isBigEndian:false);
            }while(value>=bound);
            return value;
        }

        public string NextToken(string alphabet,int length)
        {
            if(string.IsNullOrEmpty(alphabet) || alphabet.Length>256 || length<0)
                throw new ArgumentOutOfRangeException(nameof(alphabet));
            Span<char> result=length<=64?stackalloc char[length]:new char[length];
            int acceptable=256-(256%alphabet.Length);
            for(int i=0;i<length;)
            {
                if(_position==_buffer.Length)
                {
                    RandomNumberGenerator.Fill(_buffer);
                    _position=0;
                }
                byte value=_buffer[_position++];
                if(value>=acceptable)continue;
                result[i++]=alphabet[value%alphabet.Length];
            }
            return new string(result);
        }

        public void Dispose()=>CryptographicOperations.ZeroMemory(_buffer);
    }
}
