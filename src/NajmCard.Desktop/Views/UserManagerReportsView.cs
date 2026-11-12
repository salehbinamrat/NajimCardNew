using System.Globalization;
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

        var summary=UmUi.Metrics(("عدد الحسابات","—"),("الجلسات النشطة","—"),
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
        bool busy=false;
        CancellationTokenSource? pending=null;
        static long Add(long a,long b)=>a>long.MaxValue-b?long.MaxValue:a+b;

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
        bool Within(DateTime? value,DateTime? lower,DateTime? upper)
            =>(!lower.HasValue&&!upper.HasValue) || (value.HasValue &&
                (!lower.HasValue||value.Value.Date>=lower.Value.Date) &&
                (!upper.HasValue||value.Value.Date<=upper.Value.Date));

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
                target.Children.Add(UmUi.Label("لا توجد بيانات مؤرخة لهذه الفترة.",
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
            foreach(var (group,i) in counts.Take(6).Select((x,i)=>(x,i)))
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
            distribution.Children.Add(chart);
        }

        void Render()
        {
            if(accounts is null||sessions is null)return;
            var lower=FromDate();
            var upper=ToDate();
            if(lower>upper)
            {
                message.Text="فترة التقرير غير صالحة: تاريخ البداية بعد النهاية.";
                return;
            }
            var selected=package.SelectedItem?.ToString()??"كل الباقات";
            var users=accounts.Rows.Where(x=>selected=="كل الباقات"||
                x.Profile==selected).ToArray();
            // Session rows are not historically linked to a specific profile.
            // Do not apply current-user profile as if it were the session's
            // original profile; this could silently misstate historical usage.
            var dated=sessions.Rows.Where(x=>Within(x.StartedAt,lower,upper)).ToArray();
            var scope=UserManagerReportFilters.CountActiveSessions(dated);
            // RADIUS sessions do not carry a reliable historical Profile
            // association. Never present global session statistics as though
            // they were filtered by the current user's selected package.
            bool allPackages=selected=="كل الباقات";
            var amount=0L;int complete=0;
            foreach(var row in dated)
                if(row.TotalBytes is long bytes)
                {
                    amount=Add(amount,bytes);complete++;
                }
            Metric(0).Text=users.Length.ToString("N0",CultureInfo.InvariantCulture);
            Metric(1).Text=!allPackages?"غير متاح حسب الباقة":
                scope.Known==0?"غير متاح":
                scope.Active.ToString("N0",CultureInfo.InvariantCulture)+
                (scope.IsComplete?"":" (جزئي)");
            Metric(2).Text=!allPackages?"غير متاح حسب الباقة":
                complete==0?"غير متاح":UmUi.Bytes(amount)+
                (complete<dated.Length?" (جزئي)":"");
            var paymentScope=payments is null?null:
                UserManagerReportFilters.SelectApprovedPayments(
                    payments.Rows,lower,upper,allPackages?null:selected);
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
                DrawBars(traffic,dated.Where(x=>x.TotalBytes.HasValue&&x.StartedAt.HasValue)
                    .Select(x=>(x.StartedAt!.Value,(decimal)x.TotalBytes!.Value/1073741824m)),
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
            var undatedInDatabase=sessions.Rows.Count(x=>!x.StartedAt.HasValue);
            message.Text=$"RouterOS {(int)accounts.Generation} | "+
                $"الحسابات: {users.Length} | سجلات الفترة: {dated.Length} | "+
                $"المؤرخة منها: {dateKnownInScope} | بلا تاريخ في القاعدة: {undatedInDatabase}. "+
                "إيرادات البطاقات المباعة خارج User Manager ليست محسوبة. "+
                (allPackages?"":"الجلسات التاريخية غير منسوبة للباقة؛ الإيرادات حسب الباقة المثبتة في الدفع. ")+
                (paymentScope is not null&&!paymentScope.IsComplete
                    ?"هناك مدفوعات معتمدة بلا باقة أو تاريخ صالح؛ لم يُعرض مجموع مضلل. ":"")+
                (payments is not null&&!lower.HasValue&&!upper.HasValue&&
                 payments.Rows.Any(x=>!x.Date.HasValue)
                    ?"الرسم اليومي يستبعد مدفوعات بلا تاريخ.":"");
            export.IsEnabled=true;
        }

        async Task Load()
        {
            if(busy||!IsLoaded)return;
            busy=true;read.IsEnabled=false;export.IsEnabled=false;
            message.Text="يجري جلب حسابات وجلسات وباقات User Manager من الراوتر...";
            using var request=new CancellationTokenSource();pending=request;
            try
            {
                var users=await gateway.ReadUmAccountsAsync(request.Token);
                var connections=await gateway.ReadUmSessionsAsync(request.Token);
                var plans=await gateway.ReadUmPlansAsync(request.Token);
                UmDataSnapshot<UmApprovedPayment>? confirmed=null;
                paymentWarning="";
                if(users.Generation==RouterOsGeneration.V7)
                {
                    try{confirmed=await gateway.ReadUmApprovedPaymentsAsync(request.Token);}
                    catch(OperationCanceledException) when(request.IsCancellationRequested){throw;}
                    catch(Exception ex){paymentWarning=ex.Message;}
                }
                else paymentWarning="إصدار RouterOS 6 لا يدعم تقرير المدفوعات بهذا المسار.";
                if(!IsLoaded||request.IsCancellationRequested)return;
                accounts=users;sessions=connections;payments=confirmed;
                package.Items.Clear();package.Items.Add("كل الباقات");
                foreach(var plan in plans.Rows.Where(x=>x.Kind=="Profile"))
                    package.Items.Add(plan.Name);
                package.SelectedIndex=0;
                Render();
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
        period.SelectionChanged+=(_,_)=>Render();
        from.SelectedDateChanged+=(_,_)=>Render();
        until.SelectedDateChanged+=(_,_)=>Render();
        package.SelectionChanged+=(_,_)=>Render();
        export.Click+=(_,_) =>
        {
            if(accounts is null||sessions is null)return;
            var dialog=new SaveFileDialog{Filter="CSV (*.csv)|*.csv",
                DefaultExt=".csv",AddExtension=true,
                FileName="NajimCardNew-UserManager-Report-"+
                    DateTime.Now.ToString("yyyyMMdd-HHmmss")+".csv"};
            if(dialog.ShowDialog()!=true)return;
            try
            {
                var lower=FromDate();var upper=ToDate();
                var rows=sessions.Rows.Where(x=>
                    (!lower.HasValue&&!upper.HasValue)||Within(x.StartedAt,lower,upper));
                UmCsv.Export(dialog.FileName,
                    ["المستخدم","وقت الاتصال","مدة الجلسة","IP","MAC","التنزيل",
                        "الرفع","الحالة"],
                    rows.Select(x=>(IReadOnlyList<string>)[x.Name,x.Started,x.Uptime,
                        x.Address,x.Mac,UmUi.Bytes(x.DownloadBytes),
                        UmUi.Bytes(x.UploadBytes),x.Status]));
                message.Text="تم تصدير تقرير الجلسات الحقيقي دون بيانات اعتماد.";
            }
            catch(Exception ex){message.Text="فشل تصدير التقرير: "+ex.Message;}
        };
        Unloaded+=(_,_)=>pending?.Cancel();
        foreach(var chart in chartContainers)
            chart.Children.Add(UmUi.Label("اقرأ التقرير لرؤية الرسم البياني.",12,false,UmUi.Gray));
    }
}
