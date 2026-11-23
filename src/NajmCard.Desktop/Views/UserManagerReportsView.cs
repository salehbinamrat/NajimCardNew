using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using NajmCard.Core.Models;
using NajmCard.Core.Services;

namespace NajmCard.Desktop.Views;

/// <summary>
/// Real RouterOS report: approved-payment revenue is never guessed from
/// profile price. Daily transfer charts use dated accounting records only.
/// </summary>
public sealed class UserManagerReportsView : UserControl
{
    // Keep charts aligned with the same burgundy/gold palette used by
    // the rest of the app. Never introduce a second UM-only color scheme.
    private static readonly Brush[] Palette=[
        UmUi.Gold,UmUi.Blue,UmUi.GoldBright,
        UmUi.Navy,UmUi.Gray,UmUi.Ink];

    public UserManagerReportsView(RouterGateway gateway,Action back)
    {
        var header=UmUi.Header("التقارير",
            "إحصاءات حقيقية من حسابات User Manager والجلسات والمدفوعات المعتمدة.");
        var from=UmUi.DateInput("تاريخ بداية التقرير",135);
        var until=UmUi.DateInput("تاريخ نهاية التقرير",135);
        var period=UmUi.Combo(new[]{"كل الفترات","اليوم","آخر 7 أيام",
            "آخر 30 يوما","مخصص"},135);
        var package=UmUi.Combo(new[]{"كل الباقات"},145);
        var read=UmUi.Action("عرض التقرير",true,110);
        var export=UmUi.Action("تصدير التقرير CSV");
        export.IsEnabled=false;
        var filters=UmUi.Panel(UmUi.Stack(
            UmUi.Row(UmUi.Label("الفترة",12,true),period,
                UmUi.Label("من",12,true),from,
                UmUi.Label("إلى",12,true),until),
            UmUi.Row(UmUi.Label("الباقة",12,true),package,read,export)));

        var summary=UmUi.Metrics(("الحسابات الحالية","—"),("الجلسات النشطة","—"),
            ("الاستهلاك المثبت","—"),("المدفوعات المعتمدة","—"));
        TextBlock Metric(int at)=>(TextBlock)((StackPanel)
            ((DockPanel)((Border)summary.Children[at]).Child).Children[1]).Children[1];

        var charts=new Grid {MinHeight=245};
        charts.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        charts.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        charts.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        var distribution=UmUi.Stack(UmUi.Label("توزيع المستخدمين حسب الباقات",14,true));
        var traffic=UmUi.Stack(UmUi.Label("الاستهلاك اليومي من RADIUS",14,true));
        var revenue=UmUi.Stack(UmUi.Label("المدفوعات المعتمدة يوميا",14,true));
        var chartContainers=new[]{distribution,traffic,revenue};
        for(int i=0;i<3;i++)
        {
            var panel=UmUi.Panel(chartContainers[i]);
            Grid.SetColumn(panel,i);charts.Children.Add(panel);
        }
        var message=UmUi.Label(
            "اختر الفترة ثم اضغط عرض التقرير. لا تعرض هذه الشاشة بيانات افتراضية.",
            12,false,UmUi.Gray);
        var info=UmUi.Panel(UmUi.Label(
            "يتم جمع الاستهلاك من جلسات RADIUS ذات التاريخ الصالح فقط، "+
            "ولا تعني قيمة باقة المستخدم أنها دفعة مالية. "+
            "النسخ والصيانة لهما قسم مستقل يحفظ سلامة قاعدة الراوتر.",
            12,false,UmUi.Gray));
        Content=UmUi.Scroller(UmUi.Stack(header,filters,summary,charts,
            UmUi.Panel(message),info));

        UmDataSnapshot<UmAccountRow>? accounts=null;
        UmDataSnapshot<UmSessionRow>? sessions=null;
        UmDataSnapshot<UmApprovedPayment>? payments=null;
        string paymentWarning="المدفوعات لم تُقرأ بعد";
        string planWarning="";
        bool busy=false,exportBusy=false;
        CancellationTokenSource? pending=null;
        int renderVersion=0;
        IEnumerable<IReadOnlyList<string>>? displayedExport=null;

        DateTime? FromDate()
        {
            var now=DateTime.Now.Date;
            return period.SelectedItem?.ToString() switch
            {
                "اليوم"=>now,
                "آخر 7 أيام"=>now.AddDays(-6),
                "آخر 30 يوما"=>now.AddDays(-29),
                "مخصص"=>from.SelectedDate?.Date,
                _=>null
            };
        }
        DateTime? ToDate()=>period.SelectedItem?.ToString() switch
        {
            "اليوم" or "آخر 7 أيام" or "آخر 30 يوما"=>DateTime.Now.Date,
            "مخصص"=>until.SelectedDate?.Date,
            _=>null
        };
        static void ResetChart(StackPanel chart,string text)
        {
            while(chart.Children.Count>1)chart.Children.RemoveAt(1);
            chart.Children.Add(UmUi.Label(text,12,false,UmUi.Gray));
        }

        void DrawBars(StackPanel target,IEnumerable<(DateTime Date,decimal Value)> rows,
            string suffix)
        {
            while(target.Children.Count>1)target.Children.RemoveAt(1);
            var grouped=rows.GroupBy(x=>x.Date.Date)
                .Select(g=>(Date:g.Key,Value:g.Sum(x=>x.Value)))
                .OrderBy(x=>x.Date).TakeLast(12).ToArray();
            if(grouped.Length==0)
            {
                target.Children.Add(UmUi.Label("لا توجد قيم مثبتة يمكن نسبتها إلى يوم محدد في هذه الفترة.",
                    12,false,UmUi.Gray));
                return;
            }
            var peak=Math.Max(1m,grouped.Max(x=>x.Value));
            var graph=new StackPanel
            {
                Orientation=Orientation.Horizontal,
                HorizontalAlignment=HorizontalAlignment.Center,
                Margin=new Thickness(0,12,0,0)
            };
            foreach(var item in grouped)
            {
                var height=(double)(item.Value/peak)*123;
                var single=new StackPanel {Width=29,VerticalAlignment=VerticalAlignment.Bottom};
                single.Children.Add(new Border
                {
                    Height=Math.Max(2,height),Width=18,
                    Background=UmUi.Gold,
                    VerticalAlignment=VerticalAlignment.Bottom,
                    CornerRadius=new CornerRadius(3),
                    ToolTip=item.Date.ToString("yyyy-MM-dd")+" : "+
                        item.Value.ToString("N2",CultureInfo.InvariantCulture)+" "+suffix
                });
                single.Children.Add(UmUi.Label(item.Date.ToString("MM/dd"),
                    9,false,UmUi.Gray));
                graph.Children.Add(single);
            }
            target.Children.Add(graph);
        }

        void DrawDistribution(IReadOnlyList<UmAccountRow> users)
        {
            while(distribution.Children.Count>1)distribution.Children.RemoveAt(1);
            var counts=users.Where(x=>!string.IsNullOrWhiteSpace(x.Profile) &&
                x.Profile!="غير متاح")
                .GroupBy(x=>x.Profile,StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(x=>x.Count()).ToArray();
            if(counts.Length==0)
            {
                distribution.Children.Add(UmUi.Label("الباقات غير متاحة في البيانات المقروءة.",
                    12,false,UmUi.Gray));return;
            }
            int total=counts.Sum(x=>x.Count());
            var chart=new StackPanel();
            foreach(var (group,i) in counts.Select((x,i)=>(x,i)))
            {
                var pct=(double)group.Count()/Math.Max(1,total);
                var bar=new Grid {Margin=new Thickness(3,6,3,3)};
                bar.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(95)});
                bar.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
                bar.Children.Add(UmUi.Label(group.Key+" "+(pct*100).ToString("F0")+"%",
                    11,true,UmUi.Navy));
                var progress=new ProgressBar
                {
                    Value=pct*100,Maximum=100,Height=15,
                    Foreground=Palette[i%Palette.Length],
                    Background=UmUi.Light
                };
                Grid.SetColumn(progress,1);bar.Children.Add(progress);
                chart.Children.Add(bar);
            }
            distribution.Children.Add(new ScrollViewer
            {
                Content=chart,MaxHeight=220,
                VerticalScrollBarVisibility=ScrollBarVisibility.Auto
            });
        }

        async Task Render()
        {
            export.IsEnabled=false;displayedExport=null;
            var version=++renderVersion;
            if(accounts is null||sessions is null)return;
            var accountData=accounts;var sessionData=sessions;var paymentData=payments;
            var lower=FromDate();
            var upper=ToDate();
            if(lower>upper)
            {
                message.Text="فترة التقرير غير صالحة: تاريخ البداية بعد النهاية.";
                return;
            }
            var selected=package.SelectedItem?.ToString()??"كل الباقات";
            bool allPackages=selected=="كل الباقات";
            try
            {
                var prepared=await Task.Run(()=>
                {
                    var users=accountData.Rows.Where(x=>allPackages||
                        string.Equals(x.Profile,selected,StringComparison.OrdinalIgnoreCase)).ToArray();
                    var trafficScope=UserManagerReportFilters.SelectTraffic(sessionData.Rows,lower,upper,allPackages?null:selected);
                    var paymentScope=paymentData is null?null:UserManagerReportFilters.SelectApprovedPayments(
                        paymentData.Rows,lower,upper,allPackages?null:selected);
                    var exportRows=UserManagerReportCsv.Rows(users,trafficScope,paymentScope,lower,upper,
                        allPackages?null:selected,accountData.ReadLabel+" | "+sessionData.ReadLabel+(paymentData is null?"":" | "+paymentData.ReadLabel));
                    return (users,trafficScope,paymentScope,exportRows);
                });
                if(version!=renderVersion||!IsLoaded)return;
                var users=prepared.users;var trafficScope=prepared.trafficScope;
                var dated=trafficScope.Rows.Select(x=>x.Session).ToArray();
                var scope=UserManagerReportFilters.CountActiveSessions(dated);
                var complete=trafficScope.KnownCount;var amount=trafficScope.KnownBytes;
                Metric(0).Text=users.Length.ToString("N0",CultureInfo.InvariantCulture);
                Metric(1).Text=!allPackages?"غير متاح حسب الباقة":
                    scope.Known==0?"غير متاح":
                    scope.Active.ToString("N0",CultureInfo.InvariantCulture)+
                    (scope.IsComplete&&trafficScope.UncertainDateCount==0?"":" (جزئي)");
                Metric(2).Text=!allPackages?"غير متاح حسب الباقة":
                    complete==0?"غير متاح":UmUi.Bytes(amount)+
                    (trafficScope.IsComplete?"":" (جزئي)");
                var paymentScope=prepared.paymentScope;
                var paid=paymentScope?.Rows.ToArray();
                decimal verifiedRevenue=0;
                string revenueCurrency="";
                bool validRevenue=paid is not null &&
                    paymentScope!.IsComplete &&
                    UserManagerRevenueMath.TryTotalSingleCurrency(
                        paid,out verifiedRevenue,out revenueCurrency);
                Metric(3).Text=paid is null?"غير متاح":
                    paymentScope is not null&&!paymentScope.IsComplete
                        ?"غير متاح (بيانات الدفع ناقصة)":
                    !validRevenue?"عملة غير معروفة أو عملات متعددة":
                    paid.Length==0?"0 (مدفوعات)":
                    verifiedRevenue.ToString("N2",CultureInfo.InvariantCulture)+
                    " "+revenueCurrency;
                DrawDistribution(users);
                if(allPackages)
                    DrawBars(traffic,trafficScope.DailyBytes
                        .Select(x=>(x.Key,(decimal)x.Value/1073741824m)),
                        "GiB");
                else
                    ResetChart(traffic,
                        "لا يمكن نسبة جلسات RADIUS التاريخية إلى الباقة الحالية بدقة.");
                if(paid is null)
                    ResetChart(revenue,"لا تتوفر سجلات مدفوعات مؤكدة: "+paymentWarning);
                else if(paymentScope is not null&&!paymentScope.IsComplete)
                    ResetChart(revenue,
                        "لا يمكن تأكيد إيرادات الفترة أو الباقة مع سجلات دفع بلا تاريخ أو باقة معروفة.");
                else if(!validRevenue)
                    ResetChart(revenue,
                        "لا يمكن رسم الإيرادات بعملات مختلطة أو غير معروفة دون تحويل معتمد.");
                else
                    DrawBars(revenue,paid.Where(x=>x.Date.HasValue)
                        .Select(x=>(x.Date!.Value,x.Amount)),revenueCurrency);
                var dateKnownInScope=dated.Count(x=>x.StartedAt.HasValue);
                var undatedInDatabase=sessionData.Rows.Count(x=>!x.StartedAt.HasValue);
                message.Text=accountData.ReadLabel+" | جلسات: "+sessionData.ReadLabel+" | "+$"RouterOS {(int)accountData.Generation} | "+
                    $"الحسابات: {users.Length} | سجلات الفترة: {dated.Length} | "+
                    $"المؤرخة منها: {dateKnownInScope} | بلا تاريخ في القاعدة: {undatedInDatabase}. "+
                    $"سجلات لم يثبت توزيعها اليومي: {trafficScope.UnallocatedDailyCount}. "+
                    "الجلسات العابرة لحدود الفترة لا يُنسب إجماليها للفترة؛ لا يُفترض توزيع متساوٍ. "+
                    "الرسوم تعرض آخر 12 يوماً متاحاً؛ التصدير يشمل جميع سجلات المرشحات. الحسابات تعكس حالتها الحالية. "+
                    "إيرادات البطاقات المباعة خارج User Manager ليست محسوبة. "+
                    (allPackages?"":"الجلسات التاريخية غير منسوبة للباقة؛ الإيرادات حسب الباقة المثبتة في الدفع. ")+
                    (paymentScope is not null&&!paymentScope.IsComplete
                        ?"هناك مدفوعات معتمدة بلا باقة أو تاريخ صالح؛ لم يُعرض مجموع مضلل. ":"")+
                    (payments is not null&&!lower.HasValue&&!upper.HasValue&&
                     payments.Rows.Any(x=>!x.Date.HasValue)
                        ?"الرسم اليومي يستبعد مدفوعات بلا تاريخ.":"")+
                    (planWarning.Length>0?" | الباقات: "+planWarning:"");
                displayedExport=prepared.exportRows;export.IsEnabled=!exportBusy;
            }
            catch(Exception ex)
            {
                if(version!=renderVersion)return;
                displayedExport=null;export.IsEnabled=false;
                message.Text="تعذر تجهيز التقرير دون بيانات ناقصة: "+ex.Message;
            }
        }

        async Task Load()
        {
            if(busy||!IsLoaded)return;
            busy=true;read.IsEnabled=false;export.IsEnabled=false;
            renderVersion++;displayedExport=null;accounts=null;sessions=null;payments=null;
            message.Text="يجري جلب حسابات وجلسات وباقات User Manager من الراوتر...";
            using var request=new CancellationTokenSource();pending=request;
            try
            {
                var users=await Task.Run(()=>gateway.ReadUmAccountsAsync(request.Token),request.Token);
                if(!users.IsComplete)
                    throw new InvalidDataException(
                        "حسابات User Manager غير مكتملة؛ لا يمكن إصدار تقرير دقيق. "+
                        users.Diagnostic);
                var connections=await Task.Run(()=>gateway.ReadUmSessionsAsync(request.Token),request.Token);
                if(!connections.IsComplete)throw new InvalidDataException(connections.Diagnostic);
                // Package-link failures must NOT discard verified account,
                // session and payment data. An old v6 router can retain
                // orphaned links even though all profiles still exist.
                UmDataSnapshot<UmPlanRow>? plans=null;
                planWarning="";
                try
                {
                    plans=await Task.Run(()=>gateway.ReadUmPlansAsync(request.Token),request.Token);
                    if(!plans.IsComplete)planWarning=plans.Diagnostic;
                }
                catch(OperationCanceledException) when(request.IsCancellationRequested){throw;}
                catch(Exception ex)
                {
                    planWarning="تعذرت قراءة جدول الباقات: "+ex.Message+
                        " | جرى الاعتماد على أسماء الباقات المثبتة في الحسابات فقط.";
                }
                UmDataSnapshot<UmApprovedPayment>? confirmed=null;
                paymentWarning="";
                if(users.Generation==RouterOsGeneration.V7)
                {
                    try{confirmed=await Task.Run(()=>gateway.ReadUmApprovedPaymentsAsync(request.Token),request.Token);}
                    catch(OperationCanceledException) when(request.IsCancellationRequested){throw;}
                    catch(Exception ex){paymentWarning=ex.Message;}
                }
                else paymentWarning="إصدار RouterOS 6 لا يدعم تقرير المدفوعات بهذا المسار.";
                if(!IsLoaded||request.IsCancellationRequested)return;
                accounts=users;sessions=connections;payments=confirmed;
                var previousPackage=package.SelectedItem?.ToString();
                package.Items.Clear();package.Items.Add("كل الباقات");
                var available=users.Rows.Select(x=>x.Profile)
                    .Concat(plans?.Rows.Where(x=>x.Kind=="Profile").Select(x=>x.Name)
                        ??Enumerable.Empty<string>())
                    .Where(x=>!string.IsNullOrWhiteSpace(x)&&x!="غير متاح")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x=>x,StringComparer.OrdinalIgnoreCase);
                foreach(var name in available)package.Items.Add(name);
                if(previousPackage is not null&&package.Items.Contains(previousPackage))package.SelectedItem=previousPackage;
                else package.SelectedIndex=0;
                await Render();
            }
            catch(OperationCanceledException) when(request.IsCancellationRequested){}
            catch(Exception ex)
            {
                if(!IsLoaded)return;
                accounts=null;sessions=null;payments=null;
                export.IsEnabled=false;
                for(int i=0;i<4;i++)Metric(i).Text="—";
                foreach(var chart in chartContainers)ResetChart(chart,"لا توجد بيانات مؤكدة.");
                message.Text="فشلت قراءة التقرير؛ لم تُعرض أرقام افتراضية: "+ex.Message;
            }
            finally{busy=false;read.IsEnabled=true;pending=null;}
        }

        read.Click+=async(_,_)=>await Load();
        period.SelectionChanged+=async(_,_)=>await Render();
        from.SelectedDateChanged+=async(_,_)=>await Render();
        until.SelectedDateChanged+=async(_,_)=>await Render();
        package.SelectionChanged+=async(_,_)=>await Render();
        export.Click+=async(_,_) =>
        {
            var rows=displayedExport;
            if(rows is null||exportBusy)return;
            var dialog=new SaveFileDialog{Filter="CSV (*.csv)|*.csv",
                DefaultExt=".csv",AddExtension=true,
                FileName="NajimCardNew-UserManager-Report-"+
                    DateTime.Now.ToString("yyyyMMdd-HHmmss")+".csv"};
            if(dialog.ShowDialog()!=true)return;
            try
            {
                exportBusy=true;export.IsEnabled=false;
                await Task.Run(()=>UmCsv.Export(dialog.FileName,UserManagerReportCsv.Headers,rows));
                message.Text="تم تصدير لقطة التقرير المختارة وقت بدء التصدير، مع بيان الاستهلاك غير المنسوب والبيانات غير المتاحة.";
            }
            catch(Exception ex){message.Text="فشل تصدير التقرير: "+ex.Message;}
            finally{exportBusy=false;export.IsEnabled=displayedExport is not null;}
        };
        Unloaded+=(_,_)=>{renderVersion++;displayedExport=null;pending?.Cancel();};
        foreach(var chart in chartContainers)
            chart.Children.Add(UmUi.Label("اقرأ التقرير لرؤية الرسم البياني.",12,false,UmUi.Gray));
    }
}
