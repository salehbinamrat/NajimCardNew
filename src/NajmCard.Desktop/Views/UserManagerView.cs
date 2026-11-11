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
        var search=PageUi.SearchInput();
        search.ToolTip="بحث محلي في النتائج المحملة؛ لا يرسل كل حرف إلى الراوتر.";
        var searchLabel=PageUi.Label("بحث");
        var previous=PageUi.Button("السابق",false);
        var next=PageUi.Button("التالي",false);
        var pageLabel=PageUi.Status();
        pageLabel.MinWidth=190;
        var toolbar=new WrapPanel {Margin=new Thickness(0,4,0,8)};
        foreach(var element in new UIElement[]{mode,section,refresh,searchLabel,search,previous,next,pageLabel})
            toolbar.Children.Add(element);

        var status=PageUi.Status();
        status.Text="حدد القسم ثم اضغط قراءة البيانات. جميع الخيارات للقراءة فقط.";
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
        var footer=PageUi.Hint(
            "البيانات من RouterOS API مباشرة. في الإصدار 7 يرتبط المستخدم بباقة عبر user-profile "+
            "وليس من جدول المستخدمين. البيانات غير المتاحة تظهر (غير معروف)، دون تخمين.");
        var body=PageUi.Column();
        body.Children.Add(heading);
        body.Children.Add(toolbar);
        body.Children.Add(status);
        body.Children.Add(grid);
        body.Children.Add(footer);
        Content=PageUi.Wrap("إدارة User Manager",
            "قراءة مستقلة للمستخدمين والباقات والقيود والجلسات دون التأثير على الطباعة.",
            PageUi.Surface(body,"#197E9A"));

        IReadOnlyList<UserManagerCatalogRow> loaded=[];
        bool hasLoaded=false;
        int page=0;
        bool busy=false;
        CancellationTokenSource? pending=null;

        UserManagerCatalogSection SelectedSection() =>
            section.SelectedItem is ComboBoxItem {Tag:UserManagerCatalogSection value}
                ?value:UserManagerCatalogSection.Profiles;

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
        }

        void Reset()
        {
            loaded=[];
            hasLoaded=false;
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
