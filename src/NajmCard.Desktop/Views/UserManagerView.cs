using System.Windows;
using System.Windows.Controls;
using NajmCard.Core.Models;
using NajmCard.Core.Services;

namespace NajmCard.Desktop.Views;

/// <summary>
/// Independent User Manager explorer for RouterOS 6/7. All operations are read-only;
/// heavy tables load only by explicit action, with local filtering and paging.
/// </summary>
public sealed class UserManagerView : UserControl
{
    private const int PageSize=40;

    public UserManagerView(RouterGateway gateway)
    {
        var heading=PageUi.Title("إدارة User Manager");
        var mode=PageUi.Label("القسم");
        var section=new ComboBox
        {
            Width=165,MinHeight=34,Margin=new Thickness(4,2,12,2),
            FontSize=14,FontWeight=FontWeights.SemiBold
        };
        foreach(var (label,value) in new[]
        {
            ("المستخدمون",UserManagerCatalogSection.Users),
            ("الباقات",UserManagerCatalogSection.Profiles),
            ("القيود",UserManagerCatalogSection.Limitations),
            ("الجلسات",UserManagerCatalogSection.Sessions)
        })
            section.Items.Add(new ComboBoxItem {Content=label,Tag=value});
        section.SelectedIndex=1;

        var refresh=PageUi.Button("قراءة البيانات");
        var diagnose=PageUi.Button("فحص إعدادات RADIUS (v7)",false);
        var search=PageUi.SearchInput();
        search.ToolTip="بحث محلي في النتائج المحملة؛ لا يرسل كل حرف إلى الراوتر.";
        var searchLabel=PageUi.Label("بحث");
        var previous=PageUi.Button("السابق",false);
        var next=PageUi.Button("التالي",false);
        var pageLabel=PageUi.Status();
        pageLabel.MinWidth=190;
        var toolbar=new WrapPanel {Margin=new Thickness(0,4,0,8)};
        foreach(var element in new UIElement[]{mode,section,refresh,diagnose,searchLabel,search,previous,next,pageLabel})
            toolbar.Children.Add(element);

        var status=PageUi.Status();
        status.Text="حدد القسم ثم اضغط قراءة البيانات. يتم تنفيذ عمليات الإدارة بعد التأكيد فقط.";
        var grid=PageUi.Table(
            ("الاسم","Name"),("الباقة","Profile"),("الحالة","Status"),
            ("السعة","Limit"),("الوقت","Uptime"),("تنزيل","Download"),
            ("رفع","Upload"),("IP","Address"),("معلومات","Extra"));
        grid.IsReadOnly=true;
        grid.CanUserAddRows=false;
        grid.CanUserDeleteRows=false;
        grid.EnableRowVirtualization=true;
        grid.MinHeight=330;
        grid.MaxHeight=470;
        var activate=PageUi.Button("تفعيل الحساب");
        var deactivate=PageUi.Button("تعطيل الحساب");
        var profileInput=PageUi.Input();
        profileInput.MinWidth=155;
        profileInput.Width=155;
        profileInput.ToolTip="اسم باقة User Manager 7 الموجودة على الراوتر";
        var assign=PageUi.Button("إسناد/تجديد الباقة");
        var userActions=new WrapPanel {Margin=new Thickness(0,2,0,8)};
        foreach(var element in new UIElement[]
        {
            activate,deactivate,PageUi.Label("باقة التجديد (v7)"),profileInput,assign
        })
            userActions.Children.Add(element);
        var accountName=PageUi.Input();
        accountName.Width=115;
        accountName.ToolTip="اسم المستخدم الجديد دون مسافات";
        var accountPassword=new PasswordBox {Width=115,MinHeight=30,
            FlowDirection=FlowDirection.LeftToRight,Margin=new Thickness(4,2,4,2)};
        accountPassword.ToolTip="اتركها فارغة لإنشاء حساب باسم مستخدم فقط";
        var accountProfile=PageUi.Input();
        accountProfile.Width=140;
        accountProfile.ToolTip="اسم باقة User Manager v7 الموجودة مسبقا";
        var createAccount=PageUi.Button("إنشاء مستخدم v7");
        var creationBar=new WrapPanel {Margin=new Thickness(0,3,0,8)};
        foreach(var element in new UIElement[]
        {
            PageUi.Label("المستخدم الجديد"),accountName,
            PageUi.Label("كلمة المرور"),accountPassword,
            PageUi.Label("الباقة"),accountProfile,createAccount
        })
            creationBar.Children.Add(element);
        var footer=PageUi.Hint(
            "البيانات من RouterOS API مباشرة. في الإصدار 7 يرتبط المستخدم بباقة عبر user-profile "+
            "وليس من جدول المستخدمين. البيانات غير المتاحة تظهر (غير معروف)، دون تخمين.");
        var body=PageUi.Column();
        body.Children.Add(heading);
        body.Children.Add(toolbar);
        body.Children.Add(status);
        body.Children.Add(userActions);
        body.Children.Add(creationBar);
        body.Children.Add(grid);
        body.Children.Add(footer);
        Content=PageUi.Wrap("إدارة User Manager",
            "قراءة مستقلة للمستخدمين والباقات والقيود والجلسات دون التأثير على الطباعة.",
            PageUi.Surface(body,"#197E9A"));

        IReadOnlyList<UserManagerCatalogRow> loaded=[];
        bool hasLoaded=false;
        RouterOsGeneration? loadedGeneration=null;
        RouterOsGeneration? detectedGeneration=null;
        int page=0;
        bool busy=false;
        CancellationTokenSource? pending=null;

        UserManagerCatalogSection SelectedSection() =>
            section.SelectedItem is ComboBoxItem {Tag:UserManagerCatalogSection value}
                ?value:UserManagerCatalogSection.Profiles;

        UserManagerAccountSelection? SelectedUser()
        {
            if(!hasLoaded || loadedGeneration is null ||
                SelectedSection()!=UserManagerCatalogSection.Users ||
                grid.SelectedItem is not UserManagerCatalogRow row ||
                row.Status is not ("مفعل" or "معطل"))
                return null;
            return new UserManagerAccountSelection(
                loadedGeneration.Value,row.Id,row.Name,row.Status=="معطل");
        }

        void UpdateActionButtons()
        {
            var selected=SelectedUser();
            bool enabled=selected is not null && !busy;
            activate.IsEnabled=enabled && selected!.Disabled;
            deactivate.IsEnabled=enabled && !selected!.Disabled;
            profileInput.IsEnabled=enabled && loadedGeneration==RouterOsGeneration.V7;
            assign.IsEnabled=enabled && loadedGeneration==RouterOsGeneration.V7 &&
                !selected!.Disabled;
            userActions.Visibility=hasLoaded &&
                SelectedSection()==UserManagerCatalogSection.Users?
                    Visibility.Visible:Visibility.Collapsed;
            creationBar.Visibility=detectedGeneration==RouterOsGeneration.V7 &&
                SelectedSection()==UserManagerCatalogSection.Users?
                    Visibility.Visible:Visibility.Collapsed;
            createAccount.IsEnabled=!busy && creationBar.Visibility==Visibility.Visible;
            diagnose.IsEnabled=!busy && detectedGeneration==RouterOsGeneration.V7;
        }

        void Display()
        {
            var term=search.Text.Trim();
            var filtered=term.Length==0?loaded:
                loaded.Where(row=>
                    row.Name.Contains(term,StringComparison.OrdinalIgnoreCase) ||
                    row.Profile.Contains(term,StringComparison.OrdinalIgnoreCase) ||
                    row.Status.Contains(term,StringComparison.OrdinalIgnoreCase) ||
                    row.Address.Contains(term,StringComparison.OrdinalIgnoreCase) ||
                    row.Extra.Contains(term,StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            int total=filtered.Count;
            int maxPage=total==0?0:(total-1)/PageSize;
            page=Math.Clamp(page,0,maxPage);
            grid.ItemsSource=filtered.Skip(page*PageSize).Take(PageSize).ToArray();
            pageLabel.Text=hasLoaded
                ?$"نتائج محملة: {total} | صفحة {page+1} من {maxPage+1}"
                :"لم يتم تحميل البيانات";
            previous.IsEnabled=hasLoaded && page>0;
            next.IsEnabled=hasLoaded && page<maxPage;
            UpdateActionButtons();
        }

        void Reset()
        {
            loaded=[];
            hasLoaded=false;
            loadedGeneration=null;
            page=0;
            status.Text="حدد القسم ثم اضغط قراءة البيانات. لا يتم جلب قوائم المستخدمين الكبيرة تلقائيا.";
            Display();
        }

        async Task Load()
        {
            if(busy || !IsLoaded)return;
            busy=true;
            refresh.IsEnabled=false;
            section.IsEnabled=false;
            using var request=new CancellationTokenSource();
            pending=request;
            var requested=SelectedSection();
            status.Text="يجري جلب البيانات من المايكروتيك عبر API...";
            try
            {
                var result=await gateway.ReadUserManagerCatalogAsync(requested,request.Token);
                if(!IsLoaded || request.IsCancellationRequested)return;
                if(requested!=SelectedSection())return;
                loaded=result.Rows;
                loadedGeneration=result.Generation;
                detectedGeneration=result.Generation;
                hasLoaded=true;
                page=0;
                status.Text=$"RouterOS {(int)result.Generation} | تمت قراءة {loaded.Count} سجل. "+
                    "البحث والتصفح للبيانات المحملة محليا.";
                Display();
            }
            catch(OperationCanceledException) when(request.IsCancellationRequested)
            {
                // The page was closed; do not touch the UI or reconnect after logout.
            }
            catch(Exception ex)
            {
                if(!IsLoaded)return;
                // Do not retain stale rows after a failed read or unsupported menu.
                loaded=[];
                hasLoaded=false;
                loadedGeneration=null;
                page=0;
                Display();
                status.Text="تعذرت قراءة User Manager: "+ex.Message;
            }
            finally
            {
                if(ReferenceEquals(pending,request))pending=null;
                busy=false;
                refresh.IsEnabled=true;
                section.IsEnabled=true;
            }
        }

        async Task ChangeAccountAsync(bool disabled)
        {
            var selected=SelectedUser();
            if(selected is null || busy)return;
            if(!PageUi.Confirm(
                (disabled?"تعطيل":"تفعيل")+" الحساب «"+selected.Name+
                "» على RouterOS "+(int)selected.Generation+"؟"))
                return;
            busy=true;
            UpdateActionButtons();
            try
            {
                var result=await gateway.SetUserManagerAccountDisabledAsync(
                    selected,disabled);
                // After any mutation/uncertain result the last snapshot is stale.
                Reset();
                status.Text=result.Message+
                    (result.Uncertain?" يلزم فحص حساب المستخدم قبل إعادة المحاولة.":"");
            }
            catch(Exception ex)
            {
                Reset();
                status.Text="لم تُنفذ العملية: "+ex.Message;
            }
            finally
            {
                busy=false;
                UpdateActionButtons();
            }
        }

        async Task AssignProfileAsync()
        {
            var selected=SelectedUser();
            if(selected is null || selected.Generation!=RouterOsGeneration.V7 ||
                selected.Disabled || busy)return;
            string package=profileInput.Text.Trim();
            if(package.Length==0)
            {
                status.Text="ادخل اسم باقة موجودة في User Manager v7.";
                return;
            }
            if(!PageUi.Confirm(
                "إسناد باقة «"+package+"» للمستخدم «"+selected.Name+
                "»؟ قد تبدأ مدة الباقة فورا وفق خيار starts-when. "+
                "تحقق من الباقة الحالية قبل التجديد."))
                return;
            busy=true;
            UpdateActionButtons();
            try
            {
                var result=await gateway.AssignUserManagerV7ProfileAsync(selected,package);
                Reset();
                status.Text=result.Message+
                    (result.Uncertain?" افحص سجلات user-profile قبل تكرار العملية.":"");
            }
            catch(Exception ex)
            {
                Reset();
                status.Text="لم يتم تأكيد الإسناد: "+ex.Message;
            }
            finally
            {
                busy=false;
                UpdateActionButtons();
            }
        }

        async Task CreateAccountAsync()
        {
            if(busy || detectedGeneration!=RouterOsGeneration.V7 ||
                SelectedSection()!=UserManagerCatalogSection.Users)
                return;
            var name=accountName.Text.Trim();
            var password=accountPassword.Password;
            var profile=accountProfile.Text.Trim();
            if(name.Length==0 || profile.Length==0)
            {
                status.Text="حدد اسم المستخدم والباقة قبل عملية الإنشاء.";
                return;
            }
            var credentialMode=password.Length==0?"اسم مستخدم فقط":"اسم مستخدم وكلمة مرور";
            if(!PageUi.Confirm(
                "إنشاء المستخدم «"+name+"» على RouterOS 7 بباقة «"+profile+
                "» وبوضع «"+credentialMode+"»؟ ستضاف الباقة بعد إنشاء الحساب، "+
                "ولا يعتمد اتصال الإنترنت دون اختبار RADIUS فعلي."))
                return;
            busy=true;
            UpdateActionButtons();
            try
            {
                var result=await gateway.CreateUserManagerV7AccountAsync(name,password,profile);
                Reset();
                status.Text=result.Message;
                if(result.AccountCreated&&!result.ProfileAssigned)
                    status.Text+=" الحساب موجود لكن الباقة لم تثبت. لا تستخدمه ككرت جاهز.";
                if(result.Uncertain)
                    status.Text+=" نتيجة غير مؤكدة؛ افحص الراوتر ولا تعاود الإنشاء مباشرة.";
            }
            catch(Exception ex)
            {
                Reset();
                status.Text="لم يبدأ الإنشاء أو تعذر تأكيده: "+ex.Message;
            }
            finally
            {
                accountPassword.Clear();
                busy=false;
                UpdateActionButtons();
            }
        }

        diagnose.Click+=async (_,_)=>
        {
            if(busy || detectedGeneration!=RouterOsGeneration.V7)return;
            busy=true;
            UpdateActionButtons();
            status.Text="يجري فحص إعدادات User Manager وRADIUS وNAS دون تغيير الراوتر...";
            try
            {
                var result=await gateway.DiagnoseUserManagerV7RadiusAsync();
                status.Text=result.Message+
                    (result.Issues.Count==0?"":" التفاصيل: "+string.Join(" | ",result.Issues));
            }
            catch(Exception ex)
            {
                status.Text="تعذر إكمال تشخيص RADIUS: "+ex.Message;
            }
            finally
            {
                busy=false;
                UpdateActionButtons();
            }
        };
        createAccount.Click+=async (_,_)=>await CreateAccountAsync();
        grid.SelectionChanged+=(_,_)=>UpdateActionButtons();
        activate.Click+=async (_,_)=>await ChangeAccountAsync(false);
        deactivate.Click+=async (_,_)=>await ChangeAccountAsync(true);
        assign.Click+=async (_,_)=>await AssignProfileAsync();
        section.SelectionChanged+=(_,_)=>Reset();
        search.TextChanged+=(_,_)=>{page=0;Display();};
        previous.Click+=(_,_)=>{if(page>0){page--;Display();}};
        next.Click+=(_,_)=>{page++;Display();};
        refresh.Click+=async (_,_)=>await Load();
        Loaded+=async (_,_)=>
        {
            try
            {
                var generation=await gateway.ReadRouterOsGenerationAsync();
                if(IsLoaded)detectedGeneration=generation;
                if(IsLoaded)UpdateActionButtons();
                if(IsLoaded && !hasLoaded)
                    status.Text=$"تم اكتشاف RouterOS {(int)generation}. اختر البيانات ثم اضغط قراءة البيانات.";
            }
            catch(Exception ex)
            {
                if(IsLoaded)status.Text="تعذر التحقق من إصدار الراوتر: "+ex.Message;
            }
        };
        Unloaded+=(_,_)=>pending?.Cancel();
        Display();
    }
}
