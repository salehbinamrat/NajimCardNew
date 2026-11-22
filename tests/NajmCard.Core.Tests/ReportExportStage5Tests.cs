using ClosedXML.Excel;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

public sealed class ReportExportStage5Tests
{
    private sealed class Router(string flag):IRouterCommandClient
    {
        public int Requests;
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(string path,
            IReadOnlyDictionary<string,string>? attributes=null,CancellationToken cancellationToken=default)
        {
            Requests++;
            IReadOnlyList<IReadOnlyDictionary<string,string>> rows=[new Dictionary<string,string>
            {{".id","*1"},{"name","card"},{"profile","gold"},{"disabled",flag},
             {"bytes-in","0"},{"bytes-out","0"},{"limit-bytes-total","1000"}}];
            return Task.FromResult(rows);
        }
    }

    [Theory]
    [InlineData("yes",true)][InlineData("true",true)][InlineData("1",true)]
    [InlineData(" YES ",true)][InlineData("True",true)]
    [InlineData("no",false)][InlineData("false",false)][InlineData("0",false)]
    [InlineData(" FALSE ",false)]
    public async Task BooleanSpellingsUseTheSameValidationAndInterpretation(string flag,bool expected)
    {
        var router=new Router(flag);
        var users=await new RouterGateway(router).ReadUsersAsync();
        Assert.Equal(expected,Assert.Single(users).Disabled);
        Assert.Equal(1,router.Requests);
    }

    [Theory]
    [InlineData("")][InlineData("not-a-boolean")]
    public async Task UnknownAdministrativeFlagIsNeverAcceptedAsFalse(string flag)
    {
        await Assert.ThrowsAsync<InvalidDataException>(()=>new RouterGateway(new Router(flag)).ReadUsersAsync());
    }

    [Fact]
    public void PublicWorkbookDoesNotLeakCredentialsEchoedInUnstructuredRouterErrors()
    {
        var root=Directory.CreateTempSubdirectory("najm-public-errors-").FullName;
        try
        {
            var card=new Voucher("001","private-password","gold","batch",DateTimeOffset.UtcNow)
                {LastError="Router rejected password=private-password"};
            var path=Path.Combine(root,"public.xlsx");VoucherExcelExporter.Export([card],path);
            using var book=new XLWorkbook(path);
            Assert.All(book.Worksheets.SelectMany(sheet=>sheet.CellsUsed()),cell=>Assert.DoesNotContain("private-password",cell.GetString()));
        }
        finally{Directory.Delete(root,true);}
    }

    [Fact]
    public void LargeExcelContainsEveryCardInBothSheets()
    {
        var root=Directory.CreateTempSubdirectory("najm-stage5-xlsx-large-").FullName;
        try
        {
            var cards=Enumerable.Range(0,10003).Select(i=>new Voucher(i.ToString("D6"),"","gold","batch",new DateTimeOffset(2026,11,22,0,0,0,TimeSpan.Zero))).ToArray();
            var path=Path.Combine(root,"جميع الكروت.xlsx");VoucherExcelExporter.Export(cards,path);
            using var book=new XLWorkbook(path);
            Assert.Equal(10004,book.Worksheet(1).LastRowUsed()!.RowNumber());
            Assert.Equal("010002",book.Worksheet(1).Cell(10004,2).GetString());
            Assert.Equal(10004,book.Worksheet("Delivery").LastRowUsed()!.RowNumber());
        }
        finally{Directory.Delete(root,true);}
    }

    [Fact]
    public void CsvStreamsLargeUnicodeReportAndFailedEnumerationPreservesExistingFile()
    {
        var root=Directory.CreateTempSubdirectory("najm-stage5-csv-").FullName;
        try
        {
            var path=Path.Combine(root,"مسار عربي ; 'اسم'","تقرير.csv");
            SpreadsheetCsv.Export(path,["name","state"],Enumerable.Range(0,25001).Select(i=>(IReadOnlyList<string>)[i.ToString(),"\ufeff=1+1"]));
            Assert.Equal(25002,File.ReadLines(path).Count());
            Assert.All(File.ReadLines(path).Skip(1),line=>Assert.Contains("\"'\ufeff=1+1\"",line));
            var bytes=File.ReadAllBytes(path);
            static IEnumerable<IReadOnlyList<string>> Failed()
            {yield return new[]{"row","value"};throw new IOException("disk/query failure");}
            Assert.Throws<IOException>(()=>SpreadsheetCsv.Export(path,["name","state"],Failed()));
            Assert.Equal(bytes,File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!,"*.tmp"));
        }
        finally{Directory.Delete(root,true);}
    }

    [Fact]
    public void OfflineExportIncludesMoreThanTenThousandCards()
    {
        var root=Directory.CreateTempSubdirectory("najm-stage5-large-").FullName;
        try
        {
            var cards=Enumerable.Range(0,10003).Select(i=>new Voucher(i.ToString("D6"),"", "gold","batch",DateTimeOffset.UtcNow)).ToArray();
            var path=Path.Combine(root,"cards.txt");
            VoucherOfflineExporter.ExportCredentialsText(cards,path);
            Assert.Equal(10003,File.ReadLines(path).Count());
        }
        finally{Directory.Delete(root,true);}
    }

    [Fact]
    public void ExcelRetainsLegacyColumnsAndIncludesActualDeliveryMetadataAsText()
    {
        var root=Directory.CreateTempSubdirectory("najm-stage5-excel-").FullName;
        try
        {
            var card=new Voucher("00123","secret","gold","batch-1",DateTimeOffset.UtcNow,
                Service:"userman",Customer:"owner-a",DeliveryStatus:"UNKNOWN")
                {RouterId="router-a",AttemptId="attempt-1",RouterRecordId="*A",UpdatedUtc="2026-11-22T12:00:00Z",LastError="=1+1"};
            var path=Path.Combine(root,"تقرير.xlsx");VoucherExcelExporter.Export([card],path,includePasswords:true);
            using var book=new XLWorkbook(path);
            Assert.Equal(13,book.Worksheet(1).LastColumnUsed()!.ColumnNumber());
            Assert.Equal("secret",book.Worksheet(1).Cell("C2").GetString());
            Assert.True(book.Worksheets.Contains("Delivery"));
            var details=book.Worksheet("Delivery");
            var values=Enumerable.Range(1,details.LastColumnUsed()!.ColumnNumber()).ToDictionary(
                c=>details.Cell(1,c).GetString(),c=>details.Cell(2,c).GetString());
            Assert.Equal("router-a",values["router_id"]);
            Assert.Equal("UNKNOWN",values["delivery_status"]);
            Assert.Equal("owner-a",values["customer"]);
            Assert.Equal("batch-1",values["batch_id"]);
            Assert.Equal("=1+1",values["last_error"]);
            Assert.All(details.CellsUsed(),c=>Assert.False(c.HasFormula));
        }
        finally{Directory.Delete(root,true);}
    }
}
