using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NajmCard.Core.Models;
using NajmCard.Core.Services;

namespace NajmCard.Desktop.Views;

/// <summary>Actual UM accounting sessions, not Hotspot active sessions.</summary>
public sealed class UserManagerSessionsScreen : UserControl
{
    private sealed record Display(UmSessionRow Source)
    {
        public string Name=>Source.Name;
        public string Started=>Source.Started;
        public string Duration=>Source.Uptime;
        public string Address=>Source.Address;
        public string Mac=>Source.Mac;
        public string Download=>UmUi.Bytes(Source.DownloadBytes);
        public string Upload=>UmUi.Bytes(Source.UploadBytes);
        public string Total=>UmUi.Bytes(Source.TotalBytes);
        public string Status=>Source.Status;
    }

    public UserManagerSessionsScreen(RouterGateway gateway)
    {
        var header=UmUi.Header("الجلسات",
            "متابعة الجلسات الفعلية من سجلات User Manager accounting، حسب البيانات التي يعيدها الراوتر.");
        var metrics=UmUi.Metrics(("الجلسات النشطة المثبتة","—"),
            ("إجمالي سجلات الجلسات","—"),("التحميل","—"),
            ("الرفع","—"),("إجمالي الاستهلاك","—"));
        TextBlock Value(int at)=>(TextBlock)((StackPanel)
            ((DockPanel)((Border)metrics.Children[at]).Child).Children[1]).Children[1];

        var search=UmUi.SearchBox("اسم المستخدم أو IP أو MAC");
        var statusFilter=UmUi.Combo(new[]{"كل الحالات","نشطة","منتهية","غير معروف"},120);
        var start=UmUi.DateInput("من تاريخ الاتصال");
        var end=UmUi.DateInput("إلى تاريخ الاتصال");
        var searchButton=UmUi.Action("بحث",true);
        var reset=UmUi.Action("مسح");
        var refresh=UmUi.Action("تحديث من الراوتر",true,128);
        var download=UmUi.Action("تصدير CSV");
        var print=UmUi.Action("طباعة الصفحة");
        var filter=UmUi.Panel(UmUi.Stack(
            UmUi.Row(UmUi.Label("البحث",12,true),search,
                UmUi.Label("الحالة",12,true),statusFilter),
            UmUi.Row(UmUi.Label("من",12),start,UmUi.Label("إلى",12),end,
                searchButton,reset)));
        var grid=UmUi.Table(("المستخدم","Name",155),("وقت الاتصال","Started",165),
            ("المدة","Duration",95),("IP","Address",115),
            ("MAC","Mac",155),("تنزيل","Download",105),
            ("رفع","Upload",100),("الحالة","Status",90));
        grid.MinHeight=310;grid.MaxHeight=380;
        var page=0;
        var pageSize=UmUi.Combo(new[]{"20","50","100"},80);
        var previous=UmUi.Action("السابق",false,70);
        var next=UmUi.Action("التالي",false,70);
        var pageLabel=UmUi.Label("لم تُحمل الجلسات بعد",12,true,UmUi.Blue);
        var status=UmUi.Label("فحوص الجلسات لا تثبت أن محاسبة RADIUS مفعلة "+
            "إلا بوجود البيانات الموثقة؛ لا تظهر أرقام تجريبية.",
            12,false,UmUi.Gray);
        Content=UmUi.Scroller(UmUi.Stack(header,metrics,filter,
            UmUi.Panel(UmUi.Stack(
                UmUi.Row(refresh,download,print,pageSize),
                grid,UmUi.Row(previous,next,pageLabel),status))));

        UmDataSnapshot<UmSessionRow>? snapshot=null;
        Display[] filtered=[];
        bool busy=false,loadedOnce=false;
        CancellationTokenSource? pending=null;

        static long AddSaturated(long a,long b)=>a>long.MaxValue-b?long.MaxValue:a+b;
        void Update()
        {
            var key=search.Text.Trim();
            var requested=statusFilter.SelectedItem?.ToString()??"كل الحالات";
            var startDate=start.SelectedDate?.Date;
            var endDate=end.SelectedDate?.Date;
            var all=snapshot?.Rows??[];
            filtered=all.Where(row=>
                (key.Length==0 || row.Name.Contains(key,StringComparison.OrdinalIgnoreCase) ||
                    row.Address.Contains(key,StringComparison.OrdinalIgnoreCase) ||
                    row.Mac.Contains(key,StringComparison.OrdinalIgnoreCase)) &&
                (requested=="كل الحالات" || row.Status==requested) &&
                (!startDate.HasValue || row.StartedAt?.Date>=startDate) &&
                (!endDate.HasValue || row.StartedAt?.Date<=endDate))
                .Select(x=>new Display(x)).ToArray();
            int take=int.TryParse(pageSize.SelectedItem?.ToString(),out var n)?n:20;
            var maxPage=Math.Max(1,(filtered.Length+take-1)/take);
            page=Math.Clamp(page,0,maxPage-1);
            grid.ItemsSource=filtered.Skip(page*take).Take(take).ToArray();
            pageLabel.Text=$"السجلات المعروضة: {filtered.Length:N0} / {all.Count:N0}"+
                $" | الصفحة {page+1} من {maxPage}";
            previous.IsEnabled=page>0;next.IsEnabled=page+1<maxPage;
            download.IsEnabled=print.IsEnabled=filtered.Length>0&&!busy;
        }
        async Task Load()
        {
            if(busy||!IsLoaded)return;
            busy=true;refresh.IsEnabled=false;
            status.Text="جلب سجلات RADIUS accounting من RouterOS؛ قد يستغرق ذلك وقتا عند كثرتها...";
            using var request=new CancellationTokenSource();pending=request;
            try
            {
                var data=await gateway.ReadUmSessionsAsync(request.Token);
                if(!IsLoaded || request.IsCancellationRequested)return;
                snapshot=data;page=0;
                var active=UserManagerReportFilters.CountActiveSessions(data.Rows);
                // If RADIUS returns mixed known and unknown activity flags,
                // the visible count is partial and must never look exact.
                Value(0).Text=active.Known==0?"غير متاح":
                    active.Active.ToString("N0",CultureInfo.InvariantCulture)+
                    (active.IsComplete?"":" (جزئي)");
                Value(1).Text=data.Rows.Count.ToString("N0",CultureInfo.InvariantCulture);
                long down=0,up=0;int knownDown=0,knownUp=0;
                foreach(var row in data.Rows)
                {
                    if(row.DownloadBytes is long d){down=AddSaturated(down,d);knownDown++;}
                    if(row.UploadBytes is long u){up=AddSaturated(up,u);knownUp++;}
                }
                Value(2).Text=knownDown==0?"غير متاح":UmUi.Bytes(down)+
                    (knownDown<data.Rows.Count?" (جزئي)":"");
                Value(3).Text=knownUp==0?"غير متاح":UmUi.Bytes(up)+
                    (knownUp<data.Rows.Count?" (جزئي)":"");
                Value(4).Text=knownDown==data.Rows.Count && knownUp==data.Rows.Count
                    ?UmUi.Bytes(AddSaturated(down,up)):"غير متاح";
                status.Text=$"RouterOS {(int)data.Generation} | جُلب {data.Rows.Count:N0} سجل؛ "+
                    "البحث والترقيم محليان. عند اختيار تاريخ تُستبعد السجلات بلا تاريخ صالح.";
                Update();
            }
            catch(OperationCanceledException) when(request.IsCancellationRequested){}
            catch(Exception ex)
            {
                if(!IsLoaded)return;
                snapshot=null;Update();
                for(int i=0;i<5;i++)Value(i).Text="—";
                status.Text="فشلت قراءة الجلسات؛ لن نعرض عددا صفريا وهميا: "+ex.Message;
            }
            finally{pending=null;busy=false;refresh.IsEnabled=true;Update();}
        }
        refresh.Click+=async(_,_)=>await Load();
        searchButton.Click+=(_,_)=>{page=0;Update();};
        reset.Click+=(_,_)=>{search.Clear();statusFilter.SelectedIndex=0;
            start.SelectedDate=end.SelectedDate=null;page=0;Update();};
        UmUi.Debounce(search,this,()=>{page=0;Update();});
        statusFilter.SelectionChanged+=(_,_)=>{page=0;Update();};
        start.SelectedDateChanged+=(_,_)=>{page=0;Update();};
        end.SelectedDateChanged+=(_,_)=>{page=0;Update();};
        pageSize.SelectionChanged+=(_,_)=>{page=0;Update();};
        previous.Click+=(_,_)=>{page--;Update();};
        next.Click+=(_,_)=>{page++;Update();};
        download.Click+=(_,_) =>
        {
            if(filtered.Length==0)return;
            var picker=new SaveFileDialog {Filter="CSV (*.csv)|*.csv",DefaultExt=".csv",
                AddExtension=true,FileName="NajimCardNew-UM-Sessions-"+
                    DateTime.Now.ToString("yyyyMMdd-HHmmss")+".csv"};
            if(picker.ShowDialog()!=true)return;
            try
            {
                UmCsv.Export(picker.FileName,
                    ["المستخدم","وقت الاتصال","وقت الانتهاء","المدة","IP",
                        "MAC","التنزيل","الرفع","الحالة"],
                    filtered.Select(x=>(IReadOnlyList<string>)[x.Name,x.Started,
                        x.Source.Ended,x.Duration,x.Address,x.Mac,
                        x.Download,x.Upload,x.Status]));
                status.Text="تم تصدير "+filtered.Length+" جلسة فعلية إلى CSV.";
            }
            catch(Exception ex){status.Text="فشل التصدير: "+ex.Message;}
        };
        print.Click+=(_,_) =>
        {
            try
            {
                var dialog=new PrintDialog();
                if(dialog.ShowDialog()!=true)return;
                dialog.PrintVisual(grid,"User Manager sessions - displayed page");
                status.Text="أُرسلت الصفحة الحالية فقط للطابعة.";
            }
            catch(Exception ex){status.Text="تعذرت الطباعة: "+ex.Message;}
        };
        Loaded+=async(_,_)=>{if(!loadedOnce){loadedOnce=true;await Load();}};
        Unloaded+=(_,_)=>pending?.Cancel();
        Update();
    }
}
