using System.Windows;
using System.Windows.Controls;
using NajmCard.Core.Models;
using NajmCard.Core.Services;

namespace NajmCard.Desktop.Views;

/// <summary>
/// One catalog per request, with filters applied locally and results paged.
/// No background polling, unlimited scans or fabricated matches.
/// </summary>
public sealed class UserManagerSearchScreen : UserControl
{
    private sealed record Hit(string Category,string Name,string Profile,
        string Status,string Usage,string Location,string More);

    public UserManagerSearchScreen(RouterGateway gateway)
    {
        var header=UmUi.Header("البحث المتقدم",
            "بحث آمن ودقيق في سجلات RouterOS 6 و7 مع قراءة كل قسم عند الطلب.");
        var source=UmUi.Combo(new[]{"المستخدمون","الباقات","القيود","الجلسات"},145);
        var keyword=UmUi.SearchBox("اسم المستخدم أو الباقة أو عنوان IP");keyword.Width=250;
        // Build the available status options only from records actually
        // returned by the selected User Manager catalog. Hardcoded status
        // lists hide v6 'incomplete' and v7 'running' categories and offer
        // meaningless user states while searching profile definitions.
        var status=UmUi.Combo(new[]{"جميع الحالات"},130);
        status.IsEnabled=false;
        var read=UmUi.Action("قراءة البيانات",true,100);
        var clear=UmUi.Action("مسح",false);
        var table=UmUi.Table(("النوع","Category",90),("الاسم","Name",148),
            ("الباقة","Profile",148),("الحالة","Status",105),
            ("الاستهلاك","Usage",120),("العنوان","Location",130),
            ("معلومات إضافية","More",230));
        table.MinHeight=370;table.MaxHeight=460;
        var previous=UmUi.Action("السابق",false,70);
        var next=UmUi.Action("التالي",false,70);
        var pageSize=UmUi.Combo(new[]{"20","50","100"},78);
        var total=UmUi.Label("0 نتيجة",12,true,UmUi.Blue);
        var message=UmUi.Label(
            "اختر نوع البحث واضغط قراءة البيانات؛ يتم الاتصال بالراوتر فقط عند الضغط.",
            12,false,UmUi.Gray);
        Content=UmUi.Scroller(UmUi.Stack(header,
            UmUi.Panel(UmUi.Stack(
                UmUi.Row(UmUi.Label("المصدر",12,true),source,
                    UmUi.Label("بحث",12,true),keyword),
                UmUi.Row(UmUi.Label("الحالة",12,true),status,read,clear))),
            UmUi.Panel(UmUi.Stack(table,
                UmUi.Row(previous,next,UmUi.Label("عرض",12),pageSize,total),
                message))));

        Hit[] items=[];
        Hit[] visible=[];
        int page=0;
        bool busy=false,resetStatus=false;
        CancellationTokenSource? pending=null;

        void SyncStatuses()
        {
            var previous=status.SelectedItem?.ToString()??"جميع الحالات";
            var options=items.Select(x=>x.Status)
                .Where(x=>!string.IsNullOrWhiteSpace(x)&&x!="من الراوتر")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x=>x,StringComparer.Ordinal).ToArray();
            resetStatus=true;
            try
            {
                status.Items.Clear();
                status.Items.Add("جميع الحالات");
                foreach(var value in options)status.Items.Add(value);
                status.SelectedItem=status.Items.Contains(previous)
                    ?previous:"جميع الحالات";
                status.IsEnabled=options.Length>0&&!busy;
            }
            finally{resetStatus=false;}
        }

        void Draw()
        {
            var q=keyword.Text.Trim();
            var state=status.SelectedItem?.ToString()??"جميع الحالات";
            visible=items.Where(x=>(q.Length==0||
                x.Name.Contains(q,StringComparison.OrdinalIgnoreCase)||
                x.Profile.Contains(q,StringComparison.OrdinalIgnoreCase)||
                x.Location.Contains(q,StringComparison.OrdinalIgnoreCase)||
                x.More.Contains(q,StringComparison.OrdinalIgnoreCase))&&
                (state=="جميع الحالات"||x.Status==state)).ToArray();
            int take=int.TryParse(pageSize.SelectedItem?.ToString(),out var n)?n:20;
            int pages=Math.Max(1,(visible.Length+take-1)/take);
            page=Math.Clamp(page,0,pages-1);
            table.ItemsSource=visible.Skip(page*take).Take(take).ToArray();
            total.Text=$"النتائج: {visible.Length} | صفحة {page+1} من {pages}";
            previous.IsEnabled=page>0;
            next.IsEnabled=page+1<pages;
        }

        async Task Load()
        {
            if(busy||!IsLoaded)return;
            busy=true;read.IsEnabled=source.IsEnabled=false;
            status.IsEnabled=false;
            items=[];SyncStatuses();Draw();
            message.Text="يجري جلب القسم المحدد من User Manager...";
            using var request=new CancellationTokenSource();pending=request;
            try
            {
                var selected=source.SelectedItem?.ToString();
                var rows=new List<Hit>();
                int version;
                switch(selected)
                {
                    case "المستخدمون":
                    {
                        var data=await gateway.ReadUmAccountsAsync(request.Token);
                        version=(int)data.Generation;
                        foreach(var x in data.Rows)rows.Add(new Hit("مستخدم",x.Name,
                            x.Profile,x.Status,UmUi.Bytes(x.UsedBytes),x.LastSeen,x.EndTime));
                        break;
                    }
                    case "الباقات":
                    case "القيود":
                    {
                        var data=await gateway.ReadUmPlansAsync(request.Token);
                        version=(int)data.Generation;
                        foreach(var x in data.Rows.Where(x=>x.Kind==
                            (selected=="الباقات"?"Profile":"Limitation")))
                            rows.Add(new Hit(x.KindLabel,x.Name,x.AssociatedLimits,
                                "من الراوتر",x.TransferLimit,"—",
                                x.Kind=="Profile"?"السعر: "+x.Price+" | المدة: "+x.Validity:
                                    "RX: "+x.DownloadRate+" | TX: "+x.UploadRate));
                        break;
                    }
                    case "الجلسات":
                    {
                        var data=await gateway.ReadUmSessionsAsync(request.Token);
                        version=(int)data.Generation;
                        foreach(var x in data.Rows)rows.Add(new Hit("جلسة",x.Name,
                            "—",x.Status,UmUi.Bytes(x.TotalBytes),x.Address,
                            "MAC: "+x.Mac+" | بدأ: "+x.Started));
                        break;
                    }
                    default:throw new ArgumentException("حدد قسما صالحا للبحث.");
                }
                if(!IsLoaded||request.IsCancellationRequested)return;
                items=rows.ToArray();page=0;SyncStatuses();Draw();
                message.Text=$"RouterOS {version} | تم جلب {items.Length} سجلا فعليا. "+
                    "الفلترة والصفحات محلية، ولا يتم تنفيذ استعلام لكل حرف.";
            }
            catch(OperationCanceledException) when(request.IsCancellationRequested){}
            catch(Exception ex)
            {
                if(IsLoaded){items=[];SyncStatuses();Draw();
                    message.Text="تعذرت قراءة النتائج: "+ex.Message;}
            }
            finally
            {
                pending=null;busy=false;read.IsEnabled=source.IsEnabled=true;
                SyncStatuses();
            }
        }
        source.SelectionChanged+=(_,_)=>
        {
            items=[];page=0;SyncStatuses();Draw();
            message.Text="تغير مصدر البحث؛ اضغط قراءة البيانات لجلب سجلات هذا القسم.";
        };
        read.Click+=async(_,_)=>await Load();
        clear.Click+=(_,_)=>{keyword.Clear();status.SelectedIndex=0;page=0;Draw();};
        UmUi.Debounce(keyword,this,()=>{page=0;Draw();});
        status.SelectionChanged+=(_,_)=>
        {
            if(resetStatus)return;
            page=0;Draw();
        };
        pageSize.SelectionChanged+=(_,_)=>{page=0;Draw();};
        previous.Click+=(_,_)=>{page--;Draw();};
        next.Click+=(_,_)=>{page++;Draw();};
        Unloaded+=(_,_)=>pending?.Cancel();
        Draw();
    }
}
