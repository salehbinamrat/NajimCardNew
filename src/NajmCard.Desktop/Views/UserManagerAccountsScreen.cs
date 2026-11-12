using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NajmCard.Core.Models;
using NajmCard.Core.Services;

namespace NajmCard.Desktop.Views;

/// <summary>
/// Real RouterOS UM user management, styled like the user's reference.
/// All destructive actions validate an exact router identity and require
/// explicit confirmation. Unknown v7 counters are never fabricated.
/// </summary>
public sealed class UserManagerAccountsScreen : UserControl
{
    private sealed record DisplayRow(UmAccountRow Source)
    {
        public string Name=>Source.Name;
        public string Profile=>Source.Profile;
        public string Status=>Source.Status;
        public string Quota=>UmUi.Bytes(Source.TransferLimit);
        public string Used=>UmUi.Bytes(Source.UsedBytes);
        public string Remaining=>Source.Remaining=="غير متاح"?"غير متاح":
            UmUi.Bytes(long.TryParse(Source.Remaining,out var b)?b:null);
        public string End=>Source.EndTime;
        public string Sessions=>UmUi.Number(Source.ActiveSessions);
    }

    public UserManagerAccountsScreen(RouterGateway gateway,Action openLegacy)
    {
        var title=UmUi.Header("إدارة المستخدمين",
            "إدارة الكروت والحسابات عبر User Manager 6 و7، وأرقام مأخوذة من الراوتر فقط.");
        var metrics=UmUi.Metrics(("إجمالي المستخدمين","—"),
            ("المستخدمة أو ذات باقة نشطة","—"),("المعطلة","—"),("المنتهية المؤكدة","—"));
        var totalCard=(TextBlock)((StackPanel)((DockPanel)((Border)metrics.Children[0]).Child).Children[1]).Children[1];
        var usedCard=(TextBlock)((StackPanel)((DockPanel)((Border)metrics.Children[1]).Child).Children[1]).Children[1];
        var disabledCard=(TextBlock)((StackPanel)((DockPanel)((Border)metrics.Children[2]).Child).Children[1]).Children[1];
        var expiredCard=(TextBlock)((StackPanel)((DockPanel)((Border)metrics.Children[3]).Child).Children[1]).Children[1];

        var add=UmUi.Action("＋ إضافة مستخدم",true,122);
        var more=UmUi.Action("الإدارة المتقدمة",false,108);
        var export=UmUi.Action("تصدير CSV");
        var print=UmUi.Action("طباعة الصفحة");
        var delete=UmUi.Action("حذف محدد");
        var disable=UmUi.Action("تعطيل");
        var enable=UmUi.Action("تفعيل");
        var refresh=UmUi.Action("تحديث",true);
        var actions=UmUi.Row(add,more,export,print,delete,disable,enable,refresh);
        var find=UmUi.SearchBox("بحث برقم الكرت أو الاسم");
        var statusFilter=UmUi.Combo(new[]{"كل الحالات","صالح","استخدم","باقة نشطة",
            "باقة جاهزة","مفعل","معطل","منتهي","بدون باقة","غير مصنف"},115);
        var profileFilter=UmUi.Combo(new[]{"كل الباقات"},125);
        var filters=UmUi.Row(UmUi.Label("بحث",12,true),find,
            UmUi.Label("الحالة",12,true),statusFilter,
            UmUi.Label("الباقة",12,true),profileFilter);
        var table=UmUi.Table(("اسم المستخدم","Name",135),
            ("الباقة","Profile",115),("السعة","Quota",105),
            ("الاستهلاك","Used",105),("المتبقي","Remaining",105),
            ("الجلسات","Sessions",80),("تاريخ الانتهاء","End",145),
            ("الحالة","Status",110));
        table.MinHeight=300;
        table.MaxHeight=385;

        var detail=UmUi.Stack(UmUi.Label("تفاصيل الكرت",17,true),
            UmUi.Label("حدد مستخدما من الجدول",12,false));
        var detailName=(TextBlock)detail.Children[1];
        var detailProfile=UmUi.Label("الباقة: —",12);
        var detailQuota=UmUi.Label("الحجم: —",12);
        var detailUsed=UmUi.Label("الاستهلاك: —",12);
        var detailExpiry=UmUi.Label("الانتهاء: —",12);
        var detailStatus=UmUi.Label("الحالة: —",12);
        var detailSessions=UmUi.Label("الجلسات المثبتة: —",12);
        detail.Children.Add(new Separator {Margin=new Thickness(0,8)});
        foreach(var line in new[]{detailProfile,detailQuota,detailUsed,detailExpiry,
            detailStatus,detailSessions})detail.Children.Add(line);
        detail.Children.Add(UmUi.Label(
            "لا تُعرض كلمات المرور هنا؛ حالة الاستخدام لا تعني بالضرورة اتصالا حاليا.",11,false,UmUi.Gray));

        var columns=new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(230)});
        columns.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});
        var left=UmUi.Panel(UmUi.Scroller(detail));
        Grid.SetColumn(left,0);columns.Children.Add(left);
        var right=UmUi.Panel(table);Grid.SetColumn(right,1);columns.Children.Add(right);

        var pageInfo=UmUi.Label("لم تُقرأ البيانات بعد",12,true,UmUi.Blue);
        var previous=UmUi.Action("السابق",false,65);
        var next=UmUi.Action("التالي",false,65);
        var pageSize=UmUi.Combo(new[]{"20","50","100"},76);
        var footer=UmUi.Row(previous,next,UmUi.Label("عرض",12),pageSize,pageInfo);
        var message=UmUi.Label(
            "جار الاستعداد لقراءة حسابات User Manager... هذه ليست بيانات تجريبية.",
            12,false,UmUi.Gray);
        var container=UmUi.Stack(title,metrics,
            UmUi.Panel(UmUi.Stack(actions,filters,columns,footer,message)));
        Content=UmUi.Scroller(container);

        IReadOnlyList<UmAccountRow> all=[];
        DisplayRow[] filtered=[];
        RouterOsGeneration? version=null;
        CancellationTokenSource? pending=null;
        bool busy=false,everLoaded=false;
        int page=0;

        UmAccountRow? Selected()=> (table.SelectedItem as DisplayRow)?.Source;
        void Details()
        {
            var row=Selected();
            detailName.Text=row is null?"حدد مستخدما من الجدول":row.Name;
            detailProfile.Text="الباقة: "+(row?.Profile??"—");
            detailQuota.Text="الحجم: "+UmUi.Bytes(row?.TransferLimit);
            detailUsed.Text="الاستهلاك: "+UmUi.Bytes(row?.UsedBytes);
            detailExpiry.Text="الانتهاء: "+(row?.EndTime??"—");
            detailStatus.Text="الحالة: "+(row?.Status??"—");
            detailSessions.Text="الجلسات المثبتة: "+UmUi.Number(row?.ActiveSessions);
            enable.IsEnabled=row?.Disabled==true&&!busy;
            disable.IsEnabled=row?.Disabled==false&&!busy;
            delete.IsEnabled=row is not null && version==RouterOsGeneration.V6 && !busy;
            delete.ToolTip=version==RouterOsGeneration.V7
                ?"حذف v7 غير مفعل قبل إتمام اختبار التحقق من هوية المستخدم."
                :"يحذف حساب v6 واحدا بعد تأكيد الهوية والحالة.";
        }
        void Draw()
        {
            var query=find.Text.Trim();
            var state=statusFilter.SelectedItem?.ToString()??"كل الحالات";
            var profile=profileFilter.SelectedItem?.ToString()??"كل الباقات";
            filtered=all.Where(row=>
                (query.Length==0||row.Name.Contains(query,StringComparison.OrdinalIgnoreCase)||
                    row.Profile.Contains(query,StringComparison.OrdinalIgnoreCase)) &&
                (state=="كل الحالات"||row.Status==state) &&
                (profile=="كل الباقات"||row.Profile==profile))
                .Select(x=>new DisplayRow(x)).ToArray();
            int size=int.TryParse(pageSize.SelectedItem?.ToString(),out var parsed)?parsed:20;
            int pages=Math.Max(1,(filtered.Length+size-1)/size);
            page=Math.Clamp(page,0,pages-1);
            table.ItemsSource=filtered.Skip(page*size).Take(size).ToArray();
            pageInfo.Text=$"المعروض: {filtered.Length:N0} | صفحة {page+1}/{pages}"+
                (version is null?"":$" | RouterOS {(int)version}");
            previous.IsEnabled=page>0;
            next.IsEnabled=page+1<pages;
            export.IsEnabled=filtered.Length>0&&!busy;
            print.IsEnabled=filtered.Length>0&&!busy;
            Details();
        }

        async Task Load()
        {
            if(busy || !IsLoaded)return;
            busy=true;
            refresh.IsEnabled=false;
            find.IsEnabled=false;
            message.Text="يجري جلب المستخدمين والباقات من الراوتر عبر API...";
            using var request=new CancellationTokenSource();
            pending=request;
            try
            {
                var snap=await gateway.ReadUmAccountsAsync(request.Token);
                if(!IsLoaded||request.IsCancellationRequested)return;
                all=snap.Rows;version=snap.Generation;
                profileFilter.Items.Clear();profileFilter.Items.Add("كل الباقات");
                foreach(var value in all.Select(x=>x.Profile)
                    .Where(x=>x!="غير متاح")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x=>x,StringComparer.OrdinalIgnoreCase))
                    profileFilter.Items.Add(value);
                profileFilter.SelectedIndex=0;
                totalCard.Text=all.Count.ToString("N0",CultureInfo.InvariantCulture);
                usedCard.Text=all.Count(x=>x.Status is "استخدم" or "باقة نشطة")
                    .ToString("N0",CultureInfo.InvariantCulture);
                disabledCard.Text=all.Count(x=>x.Disabled==true)
                    .ToString("N0",CultureInfo.InvariantCulture);
                expiredCard.Text=all.Count(x=>x.Status=="منتهي")
                    .ToString("N0",CultureInfo.InvariantCulture);
                page=0;Draw();
                message.Text=$"تم جلب {all.Count:N0} حسابا فعليا من User Manager؛ "+
                    "الترقيم والبحث محليان بعد الجلب. راجع حالة RADIUS قبل اعتبار الكرت متصلا.";
            }
            catch(OperationCanceledException) when(request.IsCancellationRequested){}
            catch(Exception ex)
            {
                if(!IsLoaded)return;
                all=[];version=null;Draw();
                totalCard.Text=usedCard.Text=disabledCard.Text=expiredCard.Text="—";
                message.Text="فشلت قراءة الحسابات؛ لا تظهر أرقام افتراضية: "+ex.Message;
            }
            finally
            {
                pending=null;busy=false;
                refresh.IsEnabled=find.IsEnabled=true;
                Draw();
            }
        }

        async Task Change(bool value)
        {
            var chosen=Selected();
            if(chosen is null||chosen.Disabled is null||version is null||busy)return;
            if(!PageUi.Confirm((value?"تعطيل":"تفعيل")+
                " الحساب «"+chosen.Name+"» على الراوتر؟"))return;
            busy=true;refresh.IsEnabled=false;Details();
            try
            {
                var result=await gateway.SetUserManagerAccountDisabledAsync(
                    new UserManagerAccountSelection(version.Value,chosen.Id,chosen.Name,
                        chosen.Disabled.Value),value);
                message.Text=result.Message;
                // Never reuse a stale snapshot after a mutation.
                all=[];version=null;Draw();
                message.Text+=" | حدّث القائمة قبل تنفيذ إجراء آخر.";
            }
            catch(Exception ex)
            {
                all=[];version=null;Draw();
                message.Text="لم يثبت تنفيذ الأمر: "+ex.Message+"؛ حدّث القائمة قبل التكرار.";
            }
            finally{busy=false;refresh.IsEnabled=true;Details();}
        }

        add.Click+=(_,_)=>openLegacy();
        more.Click+=(_,_)=>openLegacy();
        refresh.Click+=async(_,_)=>await Load();
        enable.Click+=async(_,_)=>await Change(false);
        disable.Click+=async(_,_)=>await Change(true);
        delete.Click+=async(_,_) =>
        {
            var row=Selected();
            if(row is null||version!=RouterOsGeneration.V6||busy)return;
            // Re-read and compare the selected v6 account through the existing
            // verified mutation pipeline; never delete using an old table ID.
            if(!PageUi.Confirm("حذف حساب User Manager v6 «"+row.Name+
                "» نهائيا؟ العملية لا يمكن التراجع عنها."))return;
            busy=true;refresh.IsEnabled=false;Details();
            try
            {
                var fresh=await gateway.ReadUserManagerV6InventoryAsync();
                var target=fresh.Users.SingleOrDefault(x=>x.Id==row.Id&&
                    x.Username==row.Name);
                if(target is null||!fresh.IsComplete ||
                    target.Disabled!=row.Disabled)
                    throw new InvalidOperationException(
                        "تغيرت بيانات الحساب منذ القراءة؛ أُوقف الحذف.");
                var result=await gateway.ChangeUserManagerV6UserAsync(target,"remove");
                message.Text=result.Message+
                    (result.Uncertain?" | راجع الراوتر ولا تُعد العملية.":"");
            }
            catch(Exception ex){message.Text="لم يثبت الحذف: "+ex.Message;}
            finally
            {
                all=[];version=null;busy=false;refresh.IsEnabled=true;Draw();
                message.Text+=" | يلزم تحديث الحسابات.";
            }
        };
        table.SelectionChanged+=(_,_)=>Details();
        find.TextChanged+=(_,_)=>{page=0;Draw();};
        statusFilter.SelectionChanged+=(_,_)=>{page=0;Draw();};
        profileFilter.SelectionChanged+=(_,_)=>{page=0;Draw();};
        pageSize.SelectionChanged+=(_,_)=>{page=0;Draw();};
        previous.Click+=(_,_)=>{if(page>0){page--;Draw();}};
        next.Click+=(_,_)=>{page++;Draw();};
        export.Click+=(_,_) =>
        {
            if(filtered.Length==0)return;
            var save=new SaveFileDialog
            {
                Filter="CSV (*.csv)|*.csv",DefaultExt=".csv",AddExtension=true,
                FileName="NajimCardNew-UM-Users-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".csv"
            };
            if(save.ShowDialog()!=true)return;
            try
            {
                UmCsv.Export(save.FileName,["الاسم","الباقة","الحالة","الحجم",
                    "الاستهلاك","المتبقي","انتهاء الباقة"],
                    filtered.Select(x=>(IReadOnlyList<string>)[x.Name,x.Profile,
                        x.Status,x.Quota,x.Used,x.Remaining,x.End]));
                message.Text="تم تصدير "+filtered.Length+" حسابا دون كلمات مرور.";
            }
            catch(Exception ex){message.Text="فشل التصدير: "+ex.Message;}
        };
        print.Click+=(_,_) =>
        {
            try
            {
                var dialog=new PrintDialog();
                if(dialog.ShowDialog()!=true)return;
                dialog.PrintVisual(table,"User Manager - الصفحة المعروضة فقط");
                message.Text="تم إرسال الصفحة الحالية للطابعة؛ لم تُطبع كل الحسابات.";
            }
            catch(Exception ex){message.Text="تعذرت طباعة الجدول: "+ex.Message;}
        };
        Loaded+=async(_,_)=>{if(!everLoaded){everLoaded=true;await Load();}};
        Unloaded+=(_,_)=>pending?.Cancel();
        Draw();
    }
}
