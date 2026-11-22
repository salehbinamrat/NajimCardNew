using NajmCard.Core.Models;
using NajmCard.Desktop.Services;
using Xunit;

namespace NajmCard.Delivery.Tests;

public sealed class ReportStorageStage5Tests:IDisposable
{
    private readonly string _root=Path.Combine(Path.GetTempPath(),"najm-stage5-"+Guid.NewGuid().ToString("N"));
    private static Voucher Card(string name)=>new(name,"","gold","report",DateTimeOffset.UtcNow){RouterId="router-a"};

    [Fact]
    public void CsvIncludesEveryStoredCardBeyondTenThousand()
    {
        using var store=new VoucherStore(_root);
        store.SaveBatch(Enumerable.Range(0,10000).Select(n=>Card(n.ToString("D6"))).ToArray());
        store.SaveBatch([Card("last-10001"),Card("last-10002"),Card("last-10003")]);
        Assert.Equal(10003,store.GetVouchersForExport(false).Count);
        var path=Path.Combine(_root,"all.csv");store.ExportCsv(path,false);
        var lines=File.ReadAllLines(path);
        Assert.Equal(10004,lines.Length);
        Assert.Contains(lines,x=>x.Contains("000000"));
        Assert.Contains(lines,x=>x.Contains("last-10003"));
    }

    [Theory]
    [InlineData("=1+1")]
    [InlineData(" +SUM(1,2)")]
    [InlineData("\t@SUM(1,2)")]
    [InlineData("\u000b=2+2")]
    [InlineData("\ufeff=2+2")]
    public void DatabaseCsvTreatsUntrustedSpreadsheetCellsAsText(string value)
    {
        using var store=new VoucherStore(_root);
        store.SaveBatch([Card(value)]);
        var path=Path.Combine(_root,"safe.csv");store.ExportCsv(path,false);
        Assert.StartsWith("\"'",File.ReadAllLines(path)[1]);
    }

    [Fact]
    public void StoreAndExportAcceptUnicodeSpacesAndConnectionStringPunctuationInPaths()
    {
        var folder=Path.Combine(_root,"بيانات الشبكة;اسم='تجربة'");
        using var store=new VoucherStore(folder);store.SaveBatch([Card("00001")]);
        var output=Path.Combine(folder,"مجلد تصدير","الكروت.csv");
        store.ExportCsv(output,false);
        Assert.Contains("00001",File.ReadAllText(output));
    }

    public void Dispose(){if(Directory.Exists(_root))Directory.Delete(_root,true);}
}
