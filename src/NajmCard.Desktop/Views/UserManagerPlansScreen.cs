using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using NajmCard.Core.Models;
using NajmCard.Core.Services;

namespace NajmCard.Desktop.Views;

/// <summary>
/// Profiles / Limitations as two true RouterOS-backed tabs, with the
/// reference design's left editor and right inventory. v6 reads remain
/// available; v7 edits require verified Core mutations and confirmation.
/// </summary>
public sealed class UserManagerPlansScreen : UserControl
{
    private sealed record Display(UmPlanRow Source)
    {
        public string Name=>Source.Name;
        public string Owner=>Source.Owner.Length==0?"—":Source.Owner;
        public string Status=>"من الراوتر";
        public string Price=>Source.Price;
        public string Duration=>Source.Validity;
        public string Quota=>long.TryParse(Source.TransferLimit,out var b)
            ?UmUi.Bytes(b):"—";
        public string Speed=>Source.Kind=="Limitation"
            ?"RX: "+Source.DownloadRate+" / TX: "+Source.UploadRate:"انظر القيود";
        public string Limits=>Source.AssociatedLimits;
    }

    public UserManagerPlansScreen(RouterGateway gateway)
    {
        var header=UmUi.Header("إدارة الباقات",
            "Profiles وLimitations والربط بينهما من RouterOS، دون افتراض حصص أو سرعات.");
        var profilesTab=UmUi.Action("Profiles (الباقات)",true,136);
        var limitsTab=UmUi.Action("Limitations (القيود)",false,138);
        var add=UmUi.Action("إضافة",true);
        var remove=UmUi.Action("حذف");
        var copy=UmUi.Action("نسخ إلى نموذج");
        var refresh=UmUi.Action("تحديث",true);
        var search=UmUi.SearchBox("البحث في الباقات أو القيود");
        var table=UmUi.Table(("الاسم","Name",145),("المالك","Owner",105),("السرعة (RX/TX)","Speed",215),
            ("السعة","Quota",110),("المدة","Duration",100),
            ("السعر","Price",82),("القيود المرتبطة","Limits",190));
        table.MinHeight=325;table.MaxHeight=425;
        var pager=UmUi.Label("لا توجد بيانات محملة",12,true,UmUi.Blue);
        var prev=UmUi.Action("السابق",false,63);
        var next=UmUi.Action("التالي",false,63);
        var size=UmUi.Combo(new[]{"20","50","100"},77);
        var tableArea=UmUi.Stack(
            UmUi.Row(profilesTab,limitsTab),
            UmUi.Row(search,refresh,add,remove,copy),table,
            UmUi.Row(prev,next,UmUi.Label("عرض",12),size,pager)
        );

        var name=UmUi.SearchBox("اسم Profile أو Limitation");name.Width=185;
        var owner=UmUi.SearchBox("مالك User Manager v6 (owner)");owner.Width=185;
        var price=UmUi.SearchBox("السعر، مثال 250");price.Width=185;
        var duration=UmUi.SearchBox("مثال 30d");duration.Width=185;
        var starts=UmUi.Combo(new[]{"first-auth","assigned"},185);
        var quota=UmUi.SearchBox("سعة الباقة بالأرقام (GiB)");quota.Width=185;
        var up=UmUi.SearchBox("RX رفع العميل مثلا 256k");up.Width=185;
        var down=UmUi.SearchBox("TX تنزيل العميل مثلا 1024k");down.Width=185;
        var uptime=UmUi.SearchBox("حد المدة، مثال 30d");uptime.Width=185;
        var limitOptions=UmUi.Combo(new[]{"اختر قيدا"},185);
        var link=UmUi.Action("ربط القيد بالباقة",false,145);
        var save=UmUi.Action("حفظ",true,88);
        var clear=UmUi.Action("تفريغ",false,87);
        var alert=UmUi.Label("حدود البيانات تُدخل بالـ GiB وتحول إلى بايت؛ "+
            "السرعات من وجهة نظر MikroTik: RX رفع العميل، TX تنزيل العميل.",
            11,false,UmUi.Gray);
        // Compact horizontal editor: four labeled fields per row rather than
        // a tall, single-column input strip. Preserve existing bound controls.
        FrameworkElement Field(string label,Control control)
        {
            var block=UmUi.Stack(UmUi.Label(label,12,true),control);
            block.Margin=new Thickness(4,3,4,3);
            control.Width=double.NaN;
            control.MinWidth=0;
            control.HorizontalAlignment=HorizontalAlignment.Stretch;
            control.VerticalAlignment=VerticalAlignment.Center;
            return block;
        }
        var editorFields=new UniformGrid
        {
            Columns=4,Rows=2,HorizontalAlignment=HorizontalAlignment.Stretch,
            Margin=new Thickness(0,3,0,5)
        };
        foreach(var cell in new FrameworkElement[]
        {
            Field("اسم الباقة أو القيد",name),Field("السعر",price),
            Field("المدة",duration),Field("بدء الصلاحية",starts),
            Field("الحجم (GiB)",quota),Field("سرعة الرفع RX",up),
            Field("سرعة التحميل TX",down),Field("حد الوقت",uptime)
        })editorFields.Children.Add(cell);
        var ownerCell=Field("حساب مالك User Manager v6",owner);
        ownerCell.Visibility=Visibility.Collapsed;
        var editor=UmUi.Stack(UmUi.Label("إضافة / تعديل",16,true),
            editorFields,ownerCell,UmUi.Row(save,clear),new Separator(),
            UmUi.Row(UmUi.Label("ربط قيد موجود",12,true),limitOptions,link),
            alert);
        var editorScroll=UmUi.Scroller(editor);
        editorScroll.MaxHeight=320;

        // Use the full page width for the real profile/limitation inventory.
        // Editable fields belong BELOW the list instead of squeezing it
        // beside a narrow, permanently visible sidebar.
        var inventoryPanel=UmUi.Panel(tableArea);
        var editorPanel=UmUi.Panel(UmUi.Stack(
            UmUi.Label("تعديلات الباقات والقيود",15,true,UmUi.Blue),
            editorScroll));
        var status=UmUi.Label("لم تُقرأ الباقات بعد",12,false,UmUi.Gray);
        Content=UmUi.Scroller(UmUi.Stack(header,inventoryPanel,
            editorPanel,UmUi.Panel(status)));

        UmDataSnapshot<UmPlanRow>? snapshot=null;
        UmPlanRow? editing=null;
        string kind="Profile";
        bool busy=false,loadedOnce=false,suppressSelectionChanged=false;
        int page=0;
        Display[] filtered=[];
        CancellationTokenSource? pending=null;

        UmPlanRow? Selected()=> (table.SelectedItem as Display)?.Source;
        void SetEditorKind()
        {
            bool isProfile=kind=="Profile";
            price.IsEnabled=duration.IsEnabled=starts.IsEnabled=isProfile;
            quota.IsEnabled=up.IsEnabled=down.IsEnabled=uptime.IsEnabled=!isProfile;
            bool writable=(snapshot?.Generation is RouterOsGeneration.V6 or RouterOsGeneration.V7)
                && snapshot.IsComplete && !busy;
            ownerCell.Visibility=snapshot?.Generation==RouterOsGeneration.V6
                ?Visibility.Visible:Visibility.Collapsed;
            owner.IsEnabled=snapshot?.Generation==RouterOsGeneration.V6 &&
                editing is null && !busy;
            limitOptions.IsEnabled=link.IsEnabled=isProfile&&writable&&editing is not null;
            profilesTab.Background=isProfile?UmUi.Cyan:UmUi.Light;
            profilesTab.Foreground=isProfile?System.Windows.Media.Brushes.White:UmUi.Navy;
            limitsTab.Background=isProfile?UmUi.Light:UmUi.Cyan;
            limitsTab.Foreground=isProfile?UmUi.Navy:System.Windows.Media.Brushes.White;
        }
        void ResetForm()
        {
            editing=null;table.SelectedItem=null;
            name.Clear();owner.Clear();price.Text="0";duration.Text="30d";
            quota.Clear();up.Clear();down.Clear();uptime.Clear();
            starts.SelectedIndex=0;
            SetEditorKind();
        }
        void Draw()
        {
            var q=search.Text.Trim();
            filtered=(snapshot?.Rows??[])
                .Where(row=>row.Kind==kind &&
                    (q.Length==0||row.Name.Contains(q,StringComparison.OrdinalIgnoreCase)||
                        row.AssociatedLimits.Contains(q,StringComparison.OrdinalIgnoreCase)))
                .Select(x=>new Display(x)).ToArray();
            int take=int.TryParse(size.SelectedItem?.ToString(),out var n)?n:20;
            int pages=Math.Max(1,(filtered.Length+take-1)/take);
            page=Math.Clamp(page,0,pages-1);
            // Rebind only the visible page, keeping the edited record selected
            // without firing SelectionChanged and discarding unsaved form values.
            var pageRows=filtered.Skip(page*take).Take(take).ToArray();
            suppressSelectionChanged=true;
            try
            {
                table.ItemsSource=pageRows;
                if(editing is not null)
                {
                    var keep=pageRows.FirstOrDefault(x=>x.Source.Id==editing.Id &&
                        x.Source.Kind==editing.Kind);
                    table.SelectedItem=keep;
                    if(keep is null)editing=null;
                }
            }
            finally {suppressSelectionChanged=false;}
            pager.Text=$"إجمالي {kind}: {filtered.Length} | صفحة {page+1} من {pages}";
            prev.IsEnabled=page>0;next.IsEnabled=page+1<pages;
            bool canWrite=(snapshot?.Generation is RouterOsGeneration.V6 or RouterOsGeneration.V7)
                && snapshot.IsComplete && !busy;
            save.IsEnabled=add.IsEnabled=canWrite;
            remove.IsEnabled=copy.IsEnabled=canWrite&&Selected()!=null;
            SetEditorKind();
        }
        async Task Load(bool force=false)
        {
            if(busy||!IsLoaded)return;
            busy=true;refresh.IsEnabled=false;
            status.Text="يجري تحميل Profiles وLimitations وروابطهما من RouterOS...";
            using var request=new CancellationTokenSource();pending=request;
            try
            {
                var result=await gateway.ReadCachedUmPlansAsync(force,request.Token);
                if(!IsLoaded||request.IsCancellationRequested)return;
                snapshot=result;editing=null;page=0;
                limitOptions.Items.Clear();limitOptions.Items.Add("اختر قيدا");
                foreach(var plan in result.Rows.Where(x=>x.Kind=="Limitation"))
                    limitOptions.Items.Add(plan.Name);
                limitOptions.SelectedIndex=0;
                Draw();
                status.Text=$"RouterOS {(int)result.Generation} | Profiles: "+
                    result.Rows.Count(x=>x.Kind=="Profile")+
                    " | Limitations: "+result.Rows.Count(x=>x.Kind=="Limitation")+
                    (result.Generation==RouterOsGeneration.V6
                        ?" | إدارة v6 تتطلب owner صحيحا وتأكيد المعرف قبل التعديل وبعده."
                        :" | إدارة v7 مع إعادة قراءة النتائج بعد كل أمر.")+
                    (result.IsComplete?"":" | قراءة روابط غير مكتملة: "+result.Diagnostic);
            }
            catch(OperationCanceledException) when(request.IsCancellationRequested){}
            catch(Exception ex)
            {
                if(!IsLoaded)return;
                snapshot=null;Draw();
                status.Text="فشلت قراءة الباقات أو روابطها: "+ex.Message;
            }
            finally{pending=null;busy=false;refresh.IsEnabled=true;Draw();}
        }

        UmPlanDraft Draft()
        {
            // Preserve arbitrary RouterOS byte limits, including 1-byte and
            // non-integer-GiB quotas, instead of rounding to three decimals.
            string transfer=kind=="Limitation"
                ?UserManagerQuotaMath.ParseGiBToByteString(quota.Text)
                :"";
            return new UmPlanDraft(kind,name.Text.Trim(),price.Text.Trim(),
                duration.Text.Trim(),kind=="Profile"?starts.SelectedItem?.ToString()??"":"",
                transfer,"","",""+uptime.Text.Trim(),up.Text.Trim(),down.Text.Trim())
            {Owner=owner.Text.Trim()};
        }
        async Task Mutate(string action)
        {
            if(busy || snapshot?.IsComplete!=true ||
               snapshot.Generation is not (RouterOsGeneration.V6 or RouterOsGeneration.V7))return;
            UmPlanDraft draft;
            try{draft=Draft();}
            catch(Exception ex){status.Text=ex.Message;return;}
            if(action!="add"&&editing is null)
            {status.Text="حدد باقة حقيقية من الجدول أولا.";return;}
            if(!PageUi.Confirm(
                (action=="add"?"إنشاء":action=="set"?"تعديل":"حذف")+
                " «"+(action=="remove"?editing?.Name:draft.Name)+
                "» على RouterOS "+(int)snapshot.Generation+"؟ "+
                "لن يُعاد الأمر تلقائيا إذا انقطع الاتصال."))return;
            busy=true;Draw();
            try
            {
                var result=await gateway.ChangeUmPlanAsync(action,draft,editing);
                status.Text=result.Message+
                    (result.Uncertain?" | راجع الراوتر قبل تكرار أي أمر.":"");
                snapshot=null;editing=null;Draw();
                if(result.Success)
                {
                    busy=false;
                    await Load(force:true);
                    busy=true;
                }
            }
            catch(Exception ex)
            {
                snapshot=null;editing=null;Draw();
                status.Text="العملية غير مؤكدة أو مرفوضة: "+ex.Message;
            }
            finally{busy=false;Draw();}
        }

        async Task Link()
        {
            if(busy||snapshot?.IsComplete!=true ||
               snapshot.Generation is not (RouterOsGeneration.V6 or RouterOsGeneration.V7) ||
               kind!="Profile"||Selected() is not UmPlanRow profile)return;
            var limit=limitOptions.SelectedItem?.ToString()??"";
            if(limit=="اختر قيدا")return;
            if(!PageUi.Confirm(
                "ربط القيد «"+limit+"» بالباقة «"+profile.Name+
                "»؟ سيتم إرسال أمر إضافة رابط واحد فقط."))return;
            busy=true;Draw();
            try
            {
                var result=snapshot.Generation==RouterOsGeneration.V6
                    ?await gateway.LinkUmProfileLimitationV6Async(profile.Name,limit,profile.Owner)
                    :await gateway.LinkUmProfileLimitationAsync(profile.Name,limit);
                status.Text=result.Message;
                snapshot=null;Draw();
                if(result.Success)
                {
                    busy=false;
                    await Load(force:true);
                    busy=true;
                }
            }
            catch(Exception ex)
            {
                snapshot=null;Draw();status.Text="تعذر تأكيد الربط: "+ex.Message;
            }
            finally{busy=false;Draw();}
        }

        table.SelectionChanged+=(_,_) =>
        {
            if(suppressSelectionChanged)return;
            var row=Selected();
            if(row is null)
            {
                editing=null;
                remove.IsEnabled=copy.IsEnabled=false;
                return;
            }
            editing=row;
            name.Text=row.Name;
            owner.Text=row.Owner;
            price.Text=row.Price=="غير متاح"?"":row.Price;
            duration.Text=row.Validity=="غير متاح"?"":row.Validity;
            starts.SelectedItem=row.StartsWhen switch
            {
                "now"=>"assigned",
                "logon"=>"first-auth",
                _=>row.StartsWhen
            };
            quota.Text=long.TryParse(row.TransferLimit,out var bytes)
                ?UserManagerQuotaMath.ToEditorGiB(bytes):"";
            up.Text=row.DownloadRate=="غير متاح"?"":row.DownloadRate;
            down.Text=row.UploadRate=="غير متاح"?"":row.UploadRate;
            uptime.Text=row.UptimeLimit=="غير متاح"?"":row.UptimeLimit;
            remove.IsEnabled=copy.IsEnabled=
                (snapshot?.Generation is RouterOsGeneration.V6 or RouterOsGeneration.V7)
                && snapshot.IsComplete && !busy;
        };
        profilesTab.Click+=(_,_)=>{kind="Profile";page=0;ResetForm();Draw();};
        limitsTab.Click+=(_,_)=>{kind="Limitation";page=0;ResetForm();Draw();};
        add.Click+=(_,_)=>{ResetForm();status.Text="أدخل اسم السجل الجديد ثم اضغط حفظ.";};
        copy.Click+=(_,_)=>{editing=null;table.SelectedItem=null;name.Text+="-copy";Draw();};
        save.Click+=async(_,_)=>await Mutate(editing is null?"add":"set");
        remove.Click+=async(_,_)=>await Mutate("remove");
        clear.Click+=(_,_)=>ResetForm();
        link.Click+=async(_,_)=>await Link();
        refresh.Click+=async(_,_)=>await Load(force:true);
        UmUi.Debounce(search,this,()=>{page=0;Draw();});
        size.SelectionChanged+=(_,_)=>{page=0;Draw();};
        prev.Click+=(_,_)=>{page--;Draw();};
        next.Click+=(_,_)=>{page++;Draw();};
        Loaded+=async(_,_)=>{if(!loadedOnce){loadedOnce=true;await Load();}};
        Unloaded+=(_,_)=>pending?.Cancel();
        ResetForm();
        Draw();
    }
}
