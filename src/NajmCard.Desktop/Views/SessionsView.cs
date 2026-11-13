using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NajmCard.Core.Models;
using NajmCard.Core.Services;

namespace NajmCard.Desktop.Views;

/// <summary>
/// Three read-only views of the SAME live Hotspot active snapshot. The user
/// selects which group is visible; switching tabs never replays RouterOS
/// commands or confuses historical UM accounting with current connections.
/// </summary>
public sealed class SessionsView : UserControl
{
    public SessionsView(RouterGateway gateway)
    {
        var grid=PageUi.Table(
            ("المستخدم","User"),("المصدر","OriginLabel"),
            ("عنوان IP","Address"),("MAC","MacAddress"),
            ("السيرفر","Server"),("الدخول بواسطة","LoginBy"),
            ("المدة","Uptime"));
        grid.IsReadOnly=true;
        grid.CanUserAddRows=false;
        grid.CanUserDeleteRows=false;
        grid.EnableRowVirtualization=true;
        grid.MaxHeight=475;
        var status=PageUi.Status();
        var totals=PageUi.Status();
        var refresh=PageUi.Button("تحديث المتصلين");
        var allButton=PageUi.Button("الكل",false);
        var umButton=PageUi.Button("متصلون اليوزرمنجر",false);
        var hotspotButton=PageUi.Button("متصلون الهوتسبوت",false);
        var tabs=new WrapPanel{FlowDirection=FlowDirection.RightToLeft,
            Margin=new Thickness(0,2,0,8)};
        foreach(var button in new[]{allButton,umButton,hotspotButton})
        {
            button.MinWidth=170;
            button.Margin=new Thickness(3,2,3,2);
            tabs.Children.Add(button);
        }
        var search=PageUi.SearchInput();
        var options=new WrapPanel{FlowDirection=FlowDirection.RightToLeft};
        options.Children.Add(PageUi.Label("بحث في المتصلين"));
        options.Children.Add(search);
        options.Children.Add(refresh);
        var stack=PageUi.Column();
        stack.Children.Add(PageUi.Title("المتصلون حاليا"));
        stack.Children.Add(tabs);
        stack.Children.Add(options);
        stack.Children.Add(totals);
        stack.Children.Add(grid);
        stack.Children.Add(status);
        Content=PageUi.Wrap("المتصلون حاليا",
            "الكل أو اتصالات User Manager عبر RADIUS أو اتصالات Hotspot المحلية. "+
            "الأرقام من الجلسات الحالية وليست سجل User Manager التاريخي.",
            PageUi.Surface(stack,"#A65F7B"));

        IReadOnlyList<LiveHotspotConnection> connections=[];
        int selectedMode=0;
        bool busy=false,hasSnapshot=false;
        CancellationTokenSource? pending=null;

        void Draw()
        {
            var buttons=new[]{allButton,umButton,hotspotButton};
            for(var index=0;index<buttons.Length;index++)
            {
                bool chosen=index==selectedMode;
                buttons[index].Background=(Brush)new BrushConverter().ConvertFrom(
                    chosen?"#AF5472":"#FFF0DF")!;
                buttons[index].Foreground=chosen?Brushes.White:
                    (Brush)new BrushConverter().ConvertFrom("#471A30")!;
            }
            if(!hasSnapshot)return;
            var term=search.Text.Trim();
            var results=connections.Where(row=>
                (selectedMode==0 ||
                    selectedMode==1&&row.Origin==HotspotConnectionOrigin.UserManagerRadius ||
                    selectedMode==2&&row.Origin==HotspotConnectionOrigin.LocalHotspot) &&
                (term.Length==0 ||
                    row.User.Contains(term,StringComparison.OrdinalIgnoreCase) ||
                    row.Address.Contains(term,StringComparison.OrdinalIgnoreCase) ||
                    row.MacAddress.Contains(term,StringComparison.OrdinalIgnoreCase) ||
                    row.Server.Contains(term,StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            grid.ItemsSource=results;
            var um=connections.Count(row=>
                row.Origin==HotspotConnectionOrigin.UserManagerRadius);
            var hotspot=connections.Count(row=>
                row.Origin==HotspotConnectionOrigin.LocalHotspot);
            var unknown=connections.Count(row=>
                row.Origin==HotspotConnectionOrigin.Unknown);
            totals.Text=$"الكل: {connections.Count:N0} | "+
                $"يوزرمنجر (RADIUS): {um:N0} | هوتسبوت محلي: {hotspot:N0}"+
                (unknown>0?$" | مصدر غير محدد: {unknown:N0}":"");
            status.Text=$"المعروض: {results.Length:N0} اتصال فعلي | "+
                "مصدر اليوزرمنجر يعتمد علامة RADIUS من Hotspot Active؛ "+
                "إذا كان للراوتر خادم RADIUS آخر فقد يشمل القسم اتصالاته أيضا.";
        }

        allButton.Click+=(_,_)=>{selectedMode=0;Draw();};
        umButton.Click+=(_,_)=>{selectedMode=1;Draw();};
        hotspotButton.Click+=(_,_)=>{selectedMode=2;Draw();};
        search.TextChanged+=(_,_)=>Draw();

        async Task Load()
        {
            if(busy||!IsLoaded)return;
            busy=true;refresh.IsEnabled=false;
            hasSnapshot=false;
            connections=[];grid.ItemsSource=null;
            totals.Text="";
            status.Text="يجري جلب المتصلين الفعليين من /ip/hotspot/active ...";
            using var request=new CancellationTokenSource();
            pending=request;
            try
            {
                var live=await gateway.ReadLiveHotspotConnectionsAsync(request.Token);
                if(!IsLoaded||request.IsCancellationRequested)return;
                connections=live;
                hasSnapshot=true;
                Draw();
            }
            catch(OperationCanceledException) when(request.IsCancellationRequested)
            {
                if(IsLoaded)status.Text="ألغيت قراءة المتصلين؛ البيانات القديمة غير معتمدة.";
            }
            catch(Exception ex)
            {
                if(IsLoaded)status.Text="فشلت قراءة المتصلين؛ لم تعرض بيانات قديمة أو أعدادا صفرية: "+
                    ex.Message;
            }
            finally
            {
                if(ReferenceEquals(pending,request))pending=null;
                busy=false;refresh.IsEnabled=true;
            }
        }
        refresh.Click+=async(_,_)=>await Load();
        Loaded+=async(_,_)=>await Load();
        Unloaded+=(_,_)=>pending?.Cancel();
        Draw();
    }
}
