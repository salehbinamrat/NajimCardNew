using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NajmCard.Core.Models;
using NajmCard.Core.Services;

namespace NajmCard.Desktop.Views;

/// <summary>Live, read-only sessions with explicit source tabs.
/// User Manager historical entries are not current connections unless the
/// router explicitly marks them active.</summary>
public sealed class SessionsView : UserControl
{
    public SessionsView(RouterGateway gateway)
    {
        var all=PageUi.Button("الكل",false);
        var manager=PageUi.Button("متصلون اليوزرمنجر",false);
        var hotspot=PageUi.Button("متصلون الهوتسبوت",false);
        var refresh=PageUi.Button("تحديث المتصلين",true);
        var tabs=new UniformGrid{Columns=3,Margin=new Thickness(0,0,0,8)};
        tabs.Children.Add(all);tabs.Children.Add(manager);tabs.Children.Add(hotspot);
        var list=PageUi.Table(
            ("المصدر","Source"),("المستخدم","User"),("عنوان IP","Address"),
            ("MAC / الجهاز","MacAddress"),("المدة","Uptime"),("الحالة","Status"));
        list.IsReadOnly=true;
        list.CanUserAddRows=false;
        list.CanUserDeleteRows=false;
        list.EnableRowVirtualization=true;
        list.MaxHeight=490;
        ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Auto);
        var status=PageUi.Status();
        status.Text="اختر نوع المتصلين؛ الكل يجمع هوتسبوت ويوزر مانجر دون PPPoE.";
        var pagination=PageUi.Label("لا توجد بيانات مقروءة بعد");
        pagination.FontSize=12;
        var previous=PageUi.Button("السابق",false);
        var next=PageUi.Button("التالي",false);
        previous.MinWidth=65;next.MinWidth=65;
        var footer=new StackPanel{Orientation=Orientation.Horizontal,
            HorizontalAlignment=HorizontalAlignment.Right};
        footer.Children.Add(previous);footer.Children.Add(next);
        footer.Children.Add(pagination);
        var stack=PageUi.Column();
        stack.Children.Add(tabs);
        stack.Children.Add(refresh);
        stack.Children.Add(list);
        stack.Children.Add(footer);
        stack.Children.Add(status);
        Content=PageUi.Wrap("المتصلون حاليا",
            "الكل = هوتسبوت + جلسات User Manager المؤكدة نشطة، "+
            "مع دمج المطابقات الحقيقية حسب اسم الحساب وIP فقط.",
            PageUi.Surface(stack,"#568E78"));

        const int pageSize=100;
        string selected="all";
        int page=0;
        bool busy=false,everLoaded=false;
        IReadOnlyList<HotspotSession>? hotspotRows=null;
        IReadOnlyList<UmSessionRow>? managerRows=null;
        string hotspotError="",managerError="";
        CancellationTokenSource? pending=null;
        var maroon=(Brush)new BrushConverter().ConvertFrom("#68233D")!;
        var chosen=(Brush)new BrushConverter().ConvertFrom("#B25979")!;
        void ShowSelection()
        {
            foreach(var (button,key) in new[]
                {(all,"all"),(manager,"userman"),(hotspot,"hotspot")})
            {
                button.Background=selected==key?chosen:maroon;
                button.Foreground=Brushes.White;
                button.BorderThickness=new Thickness(selected==key?2:1);
            }
        }
        void Render()
        {
            ShowSelection();
            var records=ConnectedUsersProjection.ForView(selected,
                hotspotRows??[],managerRows??[]);
            var pages=Math.Max(1,(records.Count+pageSize-1)/pageSize);
            page=Math.Clamp(page,0,pages-1);
            list.ItemsSource=records.Skip(page*pageSize).Take(pageSize).ToArray();
            previous.IsEnabled=page>0;
            next.IsEnabled=page<pages-1;
            pagination.Text=$"المعروض: {Math.Min(pageSize,
                Math.Max(0,records.Count-page*pageSize)):N0} من "+
                $"{records.Count:N0} | الصفحة {page+1:N0} من {pages:N0}";
            var hotspotCount=hotspotRows is null?"غير متاح":
                hotspotRows.Count.ToString("N0",CultureInfo.InvariantCulture);
            var managerCount=managerRows is null?"غير متاح":
                managerRows.Count(s=>s.IsActive==true).ToString("N0",
                    CultureInfo.InvariantCulture);
            var unknown=managerRows?.Count(s=>s.IsActive is null)??0;
            status.Text=$"المصدر: {selected switch
                {
                    "hotspot"=>"الهوتسبوت",
                    "userman"=>"اليوزرمنجر",
                    _=>"الكل (دون PPPoE)"
                }} | متصلون هوتسبوت: {hotspotCount} | "+
                $"يوزر مانجر مؤكدو النشاط: {managerCount}"+
                (unknown>0?$" | سجلات يوزر مانجر بحالة غير مؤكدة: {unknown:N0} "+
                    "(غير محتسبة كمتصلين)":"")+
                (hotspotError.Length>0?" | تعذر قراءة هوتسبوت: "+hotspotError:"")+
                (managerError.Length>0?" | تعذر قراءة يوزر مانجر: "+managerError:"")+
                (busy?" | جار تحديث البيانات...":"");
        }
        async Task Load(bool force=false)
        {
            if(busy||!IsLoaded)return;
            var requested=selected;
            var needHotspot=requested!="userman"&&(force||hotspotRows is null);
            var needManager=requested!="hotspot"&&(force||managerRows is null);
            if(!needHotspot&&!needManager){Render();return;}
            busy=true;refresh.IsEnabled=false;Render();
            using var token=new CancellationTokenSource();
            pending=token;
            try
            {
                // RouterOS reads remain sequential on the authenticated session.
                // A failure in one service cannot fake a zero in the other.
                if(needHotspot)
                {
                    try
                    {
                        hotspotRows=await gateway.ReadSessionsAsync(token.Token);
                        hotspotError="";
                    }
                    catch(OperationCanceledException) when(token.IsCancellationRequested)
                    {return;}
                    catch(Exception ex)
                    {
                        hotspotRows=null;hotspotError=ex.Message;
                    }
                }
                if(needManager)
                {
                    try
                    {
                        var snapshot=await gateway.ReadUmSessionsAsync(token.Token);
                        managerRows=snapshot.Rows;
                        managerError=snapshot.Diagnostic;
                    }
                    catch(OperationCanceledException) when(token.IsCancellationRequested)
                    {return;}
                    catch(Exception ex)
                    {
                        managerRows=null;managerError=ex.Message;
                    }
                }
            }
            finally
            {
                if(ReferenceEquals(pending,token))pending=null;
                busy=false;refresh.IsEnabled=true;
                if(IsLoaded)Render();
                // A tab switch made during an old load must load the newly
                // requested source exactly once, without silently changing tabs.
                if(IsLoaded&&selected!=requested)_=Load(false);
            }
        }
        void Select(string tab)
        {
            selected=tab;page=0;Render();
            _=Load(false);
        }
        all.Click+=(_,_)=>Select("all");
        manager.Click+=(_,_)=>Select("userman");
        hotspot.Click+=(_,_)=>Select("hotspot");
        refresh.Click+=async(_,_)=>{page=0;await Load(true);};
        previous.Click+=(_,_)=>{page--;Render();};
        next.Click+=(_,_)=>{page++;Render();};
        Loaded+=async(_,_)=>
        {
            if(!everLoaded){everLoaded=true;await Load();}
        };
        Unloaded+=(_,_)=>pending?.Cancel();
        Render();
    }
}
