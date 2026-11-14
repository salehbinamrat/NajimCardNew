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

    private sealed record CardSessionDisplay(UmSessionRow Source)
    {
        public string Started=>Source.StartedAt?.ToString(
            "yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture)??Source.Started;
        public string Duration=>Source.Uptime;
        public string Status=>Source.RawStatus=="غير متاح"?
            Source.Status:Source.RawStatus;
        public string Ended=>Source.Ended;
        public string Address=>Source.Address;
        public string CallingStation=>Source.Mac;
        public string Download=>UmUi.Bytes(Source.DownloadBytes);
        public string Upload=>UmUi.Bytes(Source.UploadBytes);
        public string Total=>UmUi.Bytes(Source.TotalBytes);
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
        var export=UmUi.Action("تصدير CSV");
        var print=UmUi.Action("طباعة الصفحة");
        var delete=UmUi.Action("حذف المحدد");
        var selectPage=UmUi.Action("تحديد الكل بالصفحة");
        var clearSelection=UmUi.Action("إلغاء التحديد");
        var cardInfo=UmUi.Action("معلومات الكرت",true);
        var disable=UmUi.Action("تعطيل");
        var enable=UmUi.Action("تفعيل");
        var refresh=UmUi.Action("تحديث",true);
        var actions=UmUi.Row(add,cardInfo,selectPage,clearSelection,
            export,print,delete,disable,enable,refresh);
        var find=UmUi.SearchBox("بحث برقم الكرت أو الاسم");
        var statusFilter=UmUi.Combo(new[]{"كل الحالات"},115);
        var profileFilter=UmUi.Combo(new[]{"كل الباقات"},125);
        statusFilter.IsEnabled=profileFilter.IsEnabled=false;
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
        table.SelectionMode=DataGridSelectionMode.Extended;

        var detail=UmUi.Stack(UmUi.Label("تفاصيل الكرت",17,true),
            UmUi.Label("حدد مستخدما من الجدول",12,false));
        var detailName=(TextBlock)detail.Children[1];
        var detailProfile=UmUi.Label("الباقة: —",12);
        var detailQuota=UmUi.Label("الحجم: —",12);
        var detailUsed=UmUi.Label("الاستهلاك: —",12);
        var detailExpiry=UmUi.Label("الانتهاء: —",12);
        var detailStatus=UmUi.Label("الحالة: —",12);
        var detailSessions=UmUi.Label("الجلسات المثبتة: —",12);
        detail.Children.Add(new Separator {Margin=new Thickness(0,8,0,8)});
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
        bool busy=false,everLoaded=false,resetFilters=false;
        int page=0;

        UmAccountRow[] SelectedRows()=>table.SelectedItems.OfType<DisplayRow>()
            .Select(row=>row.Source).ToArray();
        UmAccountRow? Selected()
        {
            var rows=SelectedRows();
            return rows.Length==1?rows[0]:null;
        }
        void ShowCardInfo()
        {
            var row=Selected();
            if(row is null)return;
            var window=new Window
            {
                Title="معلومات الكرت — "+row.Name,
                Width=990,Height=685,MinWidth=760,MinHeight=550,
                WindowStartupLocation=WindowStartupLocation.CenterOwner,
                Owner=Window.GetWindow(this),
                FlowDirection=FlowDirection.RightToLeft,
                Background=UmUi.Light
            };
            var sessionDetails=UmUi.Label(
                "اضغط «عرض سجل الجلسات» لجلب سجلات هذا الكرت من RouterOS. "+
                "لا تعني الجلسات التاريخية أن الكرت متصل حاليا.",12);
            // Match the real RouterOS User Manager session table: both
            // timestamps, IP, RADIUS status, uptime, RX/TX and calling ID.
            var sessionGrid=UmUi.Table(
                ("الحالة / RADIUS","Status",175),
                ("عنوان IP","Address",120),
                ("بداية الاتصال","Started",168),
                ("نهاية الاتصال","Ended",168),
                ("المدة","Duration",108),
                ("التنزيل","Download",115),
                ("الرفع","Upload",115),
                ("معرف الجهاز / MAC","CallingStation",160),
                ("الإجمالي","Total",110));
            sessionGrid.MinHeight=250;
            sessionGrid.MaxHeight=295;
            ScrollViewer.SetHorizontalScrollBarVisibility(
                sessionGrid,ScrollBarVisibility.Auto);
            var viewSessions=UmUi.Action("عرض سجل الجلسات",true,145);
            var manage=UmUi.Action("خيارات الإدارة الأخرى",false,165);
            var close=UmUi.Action("إغلاق",false,70);
            var export=UmUi.Action("تصدير سجل CSV",false,125);
            var stateFilter=UmUi.Combo(
                new[]{"كل الحالات","نشطة","منتهية","غير معروف"},110);
            var pageSize=UmUi.Combo(new[]{"20","50","100"},70);
            var previousSessions=UmUi.Action("السابق",false,75);
            var nextSessions=UmUi.Action("التالي",false,75);
            var pageLabel=UmUi.Label("لم تُقرأ الجلسات بعد",12,true,UmUi.Blue);
            UmSessionRow[] history=[];
            UmSessionRow[] filtered=[];
            int sessionPage=0;
            bool sessionsLoaded=false;
            void RenderHistory()
            {
                if(!sessionsLoaded)return;
                var requested=stateFilter.SelectedItem?.ToString()??"كل الحالات";
                filtered=history.Where(x=>requested=="كل الحالات" ||
                    x.Status==requested).ToArray();
                int size=int.TryParse(pageSize.SelectedItem?.ToString(),out var n)?n:20;
                int pages=Math.Max(1,(filtered.Length+size-1)/size);
                sessionPage=Math.Clamp(sessionPage,0,pages-1);
                var shown=filtered.Skip(sessionPage*size).Take(size).ToArray();
                sessionGrid.ItemsSource=shown.Select(x=>new CardSessionDisplay(x)).ToArray();
                int verifiedActive=history.Count(x=>x.IsActive==true);
                int verifiedEnded=history.Count(x=>x.IsActive==false);
                int unknown=history.Length-verifiedActive-verifiedEnded;
                sessionDetails.Text=$"عدد سجلات الكرت على الراوتر: {history.Length:N0} | "+
                    $"نشطة مؤكدة: {verifiedActive:N0} | منتهية: {verifiedEnded:N0} | "+
                    $"حالة غير معروفة: {unknown:N0}. "+
                    "العدد يمثل سجلات User Manager التاريخية وليس عدد الأجهزة المتصلة حاليا.";
                pageLabel.Text=$"المعروض الآن: {shown.Length:N0} من {filtered.Length:N0} "+
                    $"بعد التصفية | صفحة {sessionPage+1:N0} من {pages:N0}";
                previousSessions.IsEnabled=sessionPage>0;
                nextSessions.IsEnabled=sessionPage+1<pages;
                export.IsEnabled=filtered.Length>0;
            }
            viewSessions.Click+=async(_,_)=>
            {
                viewSessions.IsEnabled=false;
                sessionDetails.Text="يجري جلب سجلات User Manager والتحقق من المعرفات...";
                try
                {
                    var sessions=await gateway.ReadUmSessionsForAccountAsync(
                        row.Name,row.Id);
                    if(!window.IsLoaded)return;
                    if(sessions.Generation!=version)
                    {
                        sessionsLoaded=false;
                        sessionGrid.ItemsSource=null;
                        sessionDetails.Text="تغير إصدار الراوتر؛ حدث صفحة المستخدمين قبل متابعة السجل.";
                        return;
                    }
                    // RouterOS returns exact matches by username or the
                    // verified user's internal ID; never prefix-matches.
                    history=sessions.Rows
                        .OrderByDescending(x=>x.StartedAt??DateTime.MinValue)
                        .ThenByDescending(x=>x.Id,StringComparer.Ordinal)
                        .ToArray();
                    sessionsLoaded=true;
                    sessionPage=0;
                    RenderHistory();
                    if(history.Length==0)
                        sessionDetails.Text="لم تظهر جلسات مطابقة لرقم الكرت "+
                            "بعد فحص القراءة السريعة وجميع حقول User Manager v6. "+
                            "إذا كانت ظاهرة في واجهة الويب، تحقق من مصدر السجلات "+
                            "وصلاحيات حساب API، ولا تعتمد الصفر كدليل على عدم استخدامها.";
                    else if(sessions.Diagnostic.Length>0)
                        sessionDetails.Text+=" | "+sessions.Diagnostic;
                }
                catch(Exception ex)
                {
                    sessionsLoaded=false;
                    history=[];filtered=[];
                    sessionGrid.ItemsSource=null;
                    export.IsEnabled=false;
                    pageLabel.Text="لم يتم اعتماد عدد جلسات بسبب فشل القراءة.";
                    sessionDetails.Text="تعذر قراءة سجل الجلسات؛ "+
                        "لا يُعرض عدد صفري مضلل: "+ex.Message;
                }
                finally{viewSessions.IsEnabled=true;}
            };
            stateFilter.SelectionChanged+=(_,_)=>{sessionPage=0;RenderHistory();};
            pageSize.SelectionChanged+=(_,_)=>{sessionPage=0;RenderHistory();};
            previousSessions.Click+=(_,_)=>{sessionPage--;RenderHistory();};
            nextSessions.Click+=(_,_)=>{sessionPage++;RenderHistory();};
            export.Click+=(_,_) =>
            {
                if(!sessionsLoaded||filtered.Length==0)return;
                var picker=new SaveFileDialog
                {
                    Filter="CSV (*.csv)|*.csv",DefaultExt=".csv",AddExtension=true,
                    FileName="NajimCardNew-UM-Card-Sessions-"+
                        DateTime.Now.ToString("yyyyMMdd-HHmmss")+".csv"
                };
                if(picker.ShowDialog(window)!=true)return;
                try
                {
                    UmCsv.Export(picker.FileName,
                        ["الكرت","بداية الاتصال","انتهاء الاتصال","المدة",
                            "الحالة","IP","MAC","التنزيل","الرفع","الإجمالي"],
                        filtered.Select(x=>(IReadOnlyList<string>)[
                            row.Name,x.Started,x.Ended,x.Uptime,x.Status,x.Address,
                            x.Mac,UmUi.Bytes(x.DownloadBytes),UmUi.Bytes(x.UploadBytes),
                            UmUi.Bytes(x.TotalBytes)]));
                    pageLabel.Text="تم تصدير "+filtered.Length+
                        " جلسة مثبتة دون بيانات اعتماد.";
                }
                catch(Exception ex){pageLabel.Text="فشل تصدير الجلسات: "+ex.Message;}
            };
            manage.Click+=(_,_)=>{window.Close();openLegacy();};
            close.Click+=(_,_)=>window.Close();
            var content=UmUi.Stack(
                UmUi.Header("معلومات الكرت",
                    "البيانات الفعلية من User Manager دون إظهار كلمات المرور."),
                UmUi.Panel(UmUi.Stack(
                    UmUi.Label("اسم المستخدم: "+row.Name,14,true),
                    UmUi.Row(UmUi.Label("الباقة: "+row.Profile,12),
                        UmUi.Label("الحالة: "+row.Status,12),
                        UmUi.Label("الانتهاء: "+row.EndTime,12)),
                    UmUi.Row(UmUi.Label("الحد: "+UmUi.Bytes(row.TransferLimit),12),
                        UmUi.Label("المستهلك: "+UmUi.Bytes(row.UsedBytes),12),
                        UmUi.Label("المتبقي: "+(row.Remaining=="غير متاح"?
                            "غير متاح":UmUi.Bytes(
                                long.TryParse(row.Remaining,out var left)?left:null)),12)),
                    UmUi.Label("الجلسات النشطة حسب بيانات الحساب: "+
                        UmUi.Number(row.ActiveSessions)+
                        " | آخر ظهور: "+row.LastSeen,12))),
                UmUi.Panel(UmUi.Stack(
                    UmUi.Row(viewSessions,manage,close),sessionDetails,
                    UmUi.Row(UmUi.Label("تصفية الحالة",12),stateFilter,
                        UmUi.Label("صفوف",12),pageSize,export),
                    sessionGrid,
                    UmUi.Row(previousSessions,nextSessions,pageLabel))));
            window.Content=UmUi.Scroller(content);
            window.ShowDialog();
        }
        void SyncFilters()
        {
            var oldStatus=statusFilter.SelectedItem?.ToString()??"كل الحالات";
            var oldProfile=profileFilter.SelectedItem?.ToString()??"كل الباقات";
            var states=all.Select(x=>x.Status)
                .Where(x=>!string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x=>x,StringComparer.Ordinal).ToArray();
            var profiles=all.Select(x=>x.Profile)
                .Where(x=>!string.IsNullOrWhiteSpace(x)&&x!="غير متاح")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();
            resetFilters=true;
            try
            {
                statusFilter.Items.Clear();
                statusFilter.Items.Add("كل الحالات");
                foreach(var name in states)statusFilter.Items.Add(name);
                statusFilter.SelectedItem=statusFilter.Items.Contains(oldStatus)
                    ?oldStatus:"كل الحالات";
                profileFilter.Items.Clear();
                profileFilter.Items.Add("كل الباقات");
                foreach(var name in profiles)profileFilter.Items.Add(name);
                profileFilter.SelectedItem=profileFilter.Items.Contains(oldProfile)
                    ?oldProfile:"كل الباقات";
                statusFilter.IsEnabled=all.Count>0&&!busy;
                profileFilter.IsEnabled=all.Count>0&&!busy;
            }
            finally{resetFilters=false;}
        }
        void Details()
        {
            var selection=SelectedRows();
            var row=selection.Length==1?selection[0]:null;
            detailName.Text=selection.Length>1?
                $"تم تحديد {selection.Length} مستخدما من الصفحة":
                row is null?"حدد مستخدما من الجدول":row.Name;
            detailProfile.Text="الباقة: "+(row?.Profile??"—");
            detailQuota.Text="الحجم: "+UmUi.Bytes(row?.TransferLimit);
            detailUsed.Text="الاستهلاك: "+UmUi.Bytes(row?.UsedBytes);
            detailExpiry.Text="الانتهاء: "+(row?.EndTime??"—");
            detailStatus.Text="الحالة: "+(row?.Status??"—");
            detailSessions.Text="الجلسات المثبتة: "+UmUi.Number(row?.ActiveSessions);
            enable.IsEnabled=row?.Disabled==true&&!busy;
            disable.IsEnabled=row?.Disabled==false&&!busy;
            delete.IsEnabled=selection.Length>0 &&
                version==RouterOsGeneration.V6 && !busy;
            delete.ToolTip=version==RouterOsGeneration.V7
                ?"حذف v7 غير مفعل قبل إتمام اختبار التحقق من هوية المستخدم."
                :"يحذف حسابات v6 المحددة مع فحص مستقل لكل حساب وتأكيد صريح.";
            clearSelection.IsEnabled=selection.Length>0&&!busy;
            cardInfo.IsEnabled=row is not null&&!busy;
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
            selectPage.IsEnabled=table.Items.Count>0&&!busy;
            Details();
        }

        async Task Load()
        {
            if(busy || !IsLoaded)return;
            busy=true;
            refresh.IsEnabled=false;
            find.IsEnabled=false;
            statusFilter.IsEnabled=profileFilter.IsEnabled=false;
            message.Text="يجري جلب المستخدمين والباقات من الراوتر عبر API...";
            using var request=new CancellationTokenSource();
            pending=request;
            try
            {
                var snap=await gateway.ReadUmAccountsAsync(request.Token);
                if(!IsLoaded||request.IsCancellationRequested)return;
                all=snap.Rows;version=snap.Generation;
                SyncFilters();
                if(!snap.IsComplete)
                {
                    // The router returned rows, but not a trustworthy complete
                    // usage inventory. Show them for inspection without treating
                    // their count or derived states as verified statistics.
                    totalCard.Text=usedCard.Text=disabledCard.Text=expiredCard.Text="—";
                    page=0;Draw();
                    message.Text="القراءة غير مكتملة؛ تم عرض الصفوف للفحص فقط، "+
                        "ولم تعتمد أعداد الحسابات أو حالات الاستخدام. "+snap.Diagnostic;
                    return;
                }
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
                all=[];version=null;SyncFilters();Draw();
                totalCard.Text=usedCard.Text=disabledCard.Text=expiredCard.Text="—";
                message.Text="فشلت قراءة الحسابات؛ لا تظهر أرقام افتراضية: "+ex.Message;
            }
            finally
            {
                pending=null;busy=false;
                refresh.IsEnabled=find.IsEnabled=true;
                SyncFilters();Draw();
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
                all=[];version=null;SyncFilters();Draw();
                message.Text+=" | حدّث القائمة قبل تنفيذ إجراء آخر.";
            }
            catch(Exception ex)
            {
                all=[];version=null;SyncFilters();Draw();
                message.Text="لم يثبت تنفيذ الأمر: "+ex.Message+"؛ حدّث القائمة قبل التكرار.";
            }
            finally{busy=false;refresh.IsEnabled=true;SyncFilters();Details();}
        }

        add.Click+=(_,_)=>openLegacy();
        refresh.Click+=async(_,_)=>await Load();
        enable.Click+=async(_,_)=>await Change(false);
        disable.Click+=async(_,_)=>await Change(true);
        delete.Click+=async(_,_) =>
        {
            var rows=SelectedRows();
            if(rows.Length==0||rows.Length>100||
                version!=RouterOsGeneration.V6||busy)return;
            var warning=rows.Length==1
                ?"حذف حساب User Manager v6 «"+rows[0].Name+"» نهائيا؟"
                :$"حذف {rows.Length} حسابا محددا من User Manager v6 نهائيا؟";
            if(!PageUi.Confirm(warning+
                " لا يمكن التراجع عن الحذف. تحقق من الاختيار قبل الاستمرار."))
                return;
            busy=true;refresh.IsEnabled=false;Details();
            var removed=0;
            try
            {
                var fresh=await gateway.ReadUserManagerV6InventoryAsync();
                if(!fresh.IsComplete)
                    throw new InvalidOperationException("البيانات غير مكتملة؛ أُوقف الحذف.");
                var targets=rows.Select(row=>fresh.Users.SingleOrDefault(x=>
                    x.Id==row.Id&&x.Username==row.Name&&x.Disabled==row.Disabled))
                    .ToArray();
                if(targets.Any(x=>x is null))
                    throw new InvalidOperationException(
                        "تغيرت هوية حساب أو حالته منذ القراءة؛ لم يبدأ الحذف.");
                foreach(var target in targets)
                {
                    // This existing command verifies the exact v6 ID, name,
                    // profile and disabled state before and after each write.
                    var result=await gateway.ChangeUserManagerV6UserAsync(target!,"remove");
                    if(!result.Success)
                    {
                        message.Text=$"توقف الحذف بعد {removed} حسابا من أصل {rows.Length}: "+
                            result.Diagnostic+
                            (result.UncertainOutcome?
                                " | لا تعِد العملية قبل فحص الراوتر.":"");
                        return;
                    }
                    removed++;
                }
                message.Text=$"حُذف {removed} حسابا وتأكد غياب كل حساب من الراوتر.";
            }
            catch(Exception ex)
            {
                message.Text=$"توقف الحذف بعد تأكيد حذف {removed} حسابا: "+
                    ex.Message+" | تحقق من الحالة قبل إعادة المحاولة.";
            }
            finally
            {
                all=[];version=null;busy=false;refresh.IsEnabled=true;
                SyncFilters();Draw();
                message.Text+=" | حدّث القائمة لقراءة البيانات الجديدة.";
            }
        };
        table.SelectionChanged+=(_,_)=>Details();
        selectPage.Click+=(_,_)=>table.SelectAll();
        clearSelection.Click+=(_,_)=>table.UnselectAll();
        cardInfo.Click+=(_,_)=>ShowCardInfo();
        UmUi.Debounce(find,this,()=>{page=0;Draw();});
        statusFilter.SelectionChanged+=(_,_)=>{if(resetFilters)return;page=0;Draw();};
        profileFilter.SelectionChanged+=(_,_)=>{if(resetFilters)return;page=0;Draw();};
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
