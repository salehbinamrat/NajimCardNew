using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using NajmCard.Desktop.Services;
using NajmCard.Desktop.Views;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if(args.Length!=1)throw new ArgumentException("Usage: VoucherPrintBenchmarks output.jsonl");
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        // Load the same resources as App.xaml without starting the production
        // application (login, licensing, network and physical printing).
        foreach(var theme in new[]{"BurgundyGold","VectorIcons"})
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source=new Uri($"pack://application:,,,/NajmCard;component/Themes/{theme}.xaml")
            });
        // Keep the application dispatcher pumping just as it does in the real
        // UI while the archive renderer works on its separate STA thread.
        app.Startup+=async(_,_)=>
        {
            try{await MeasureAsync(args[0]);app.Shutdown();}
            catch(Exception ex){Console.Error.WriteLine(ex);app.Shutdown(1);}
        };
        return app.Run();
    }

    private static async Task MeasureAsync(string destination)
    {
        var target=Path.GetFullPath(destination);Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        using var output=new StreamWriter(target,false);
        foreach(var size in new[]{1,1,100,1000,10000}.Select((size,index)=>(size,index)))
        {
            var root=Path.Combine(Path.GetTempPath(),"najm-print-benchmark",Guid.NewGuid().ToString("N"));
            try
            {
                using var store=new VoucherStore(root);
                var cards=Enumerable.Range(1,size.size).Select(n=>new Voucher("SIM"+n.ToString("D10"),"","gold","simulation",
                    DateTimeOffset.Parse("2026-01-01T00:00:00Z"),SentToRouter:true,DeliveryStatus:"SENT"){RouterId="simulation-only"}).ToArray();
                // Confirmed synthetic fixtures; no RouterOS client is constructed.
                store.SaveBatch(cards);
                var appearance=new VoucherCardAppearance(ShowQr:false,ShowPassword:false);
                var printer=new PrintingView(store,selectedBatch:()=>cards,selectedAppearance:()=>appearance);
                var path=Path.Combine(root,"cards.pdf");var options=new CardPrintOptions();
                using var operation=new VoucherPipelineOperation();var timer=Stopwatch.StartNew();
                var method=typeof(PrintingView).GetMethod("SaveBatchArchivePdfAsync",BindingFlags.Instance|BindingFlags.NonPublic)
                    ??throw new InvalidOperationException("Actual archive renderer is missing");
                Console.WriteLine($"WPF {size.size}: starting actual archive render (warmup={size.index==0})");
                await ((Task)method.Invoke(printer,[cards,appearance,options,path])!).WaitAsync(TimeSpan.FromMinutes(10));
                timer.Stop();var measured=operation.Metrics.Snapshot();
                using var pdf=UglyToad.PdfPig.PdfDocument.Open(path);
                var plan=CardPrintLayoutPlanner.Calculate(options,cards.Length);
                if(pdf.NumberOfPages!=plan.PdfPages||!pdf.GetPage(1).Text.Contains(cards[0].Username)||
                    !pdf.GetPage(pdf.NumberOfPages).Text.Contains(cards[^1].Username))
                    throw new InvalidDataException("Printed pages or endpoint credentials were lost");
                if(size.index>0)
                {
                    output.WriteLine(JsonSerializer.Serialize(new{size=size.size,pages=pdf.NumberOfPages,
                        print_ms=measured.StageMilliseconds["Print"],save_pdf_ms=measured.StageMilliseconds["Save"],
                        elapsed_ms=timer.Elapsed.TotalMilliseconds,bytes=new FileInfo(path).Length,samples=1,physical_printer=false}));
                    output.Flush();Console.WriteLine($"WPF {size.size}: {pdf.NumberOfPages} verified PDF pages");
                }
            }
            finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        }
    }
}
