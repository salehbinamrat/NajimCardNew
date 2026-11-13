using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Controls.Primitives;
using NajmCard.Core.Models;
using NajmCard.Core.Services;

namespace NajmCard.Desktop.Views;

/// <summary>
/// Single authoritative Hotspot server management surface, embedded both in
/// the sidebar Servers page and the maintenance tab. All changes go through
/// exact-ID RouterOS readback, not a broad enable/disable command.
/// </summary>
public sealed class ServersView : UserControl
{
    public ServersView(RouterGateway gateway,bool embedded=false)
    {
        var grid=PageUi.Table(
            ("السيرفر","Name"),("الواجهة","Interface"),
            ("البروفايل","Profile"),("الحالة","StateLabel"),
            ("معطل","Disabled"),("غير صالح","Invalid"),("قيد التشغيل","Running"));
        grid.IsReadOnly=true;
        grid.CanUserAddRows=false;
        grid.CanUserDeleteRows=false;
        grid.SelectionMode=DataGridSelectionMode.Extended;
        grid.SelectionUnit=DataGridSelectionUnit.FullRow;
        grid.EnableRowVirtualization=true;

        // A selection checkbox avoids requiring Ctrl+click for partial batches.
        // It mirrors DataGridRow.IsSelected, never mutates a router property.
        var checkFactory=new FrameworkElementFactory(typeof(CheckBox));
        checkFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty,
            HorizontalAlignment.Center);
        checkFactory.SetValue(FrameworkElement.VerticalAlignmentProperty,
            VerticalAlignment.Center);
        checkFactory.SetBinding(ToggleButton.IsCheckedProperty,new Binding("IsSelected")
        {
            Mode=BindingMode.TwoWay,
            RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,
                typeof(DataGridRow),1)
        });
        var checkTemplate=new DataTemplate{VisualTree=checkFactory};
        grid.Columns.Insert(0,new DataGridTemplateColumn
        {
            Header="تحديد",CellTemplate=checkTemplate,
            Width=new DataGridLength(76)
        });

        var refresh=PageUi.Button("تحديث حالة السيرفرات");
        var enable=PageUi.Button("تفعيل المحدد",false);
        var disable=PageUi.Button("تعطيل المحدد",false);
        var selectAll=PageUi.Button("تحديد الكل",false);
        var clear=PageUi.Button("إلغاء التحديد",false);
        var buttons=new WrapPanel{Orientation=Orientation.Horizontal};
        foreach(var btn in new[]{refresh,enable,disable,selectAll,clear})
        {
            btn.Margin=new Thickness(3,2,3,3);
            buttons.Children.Add(btn);
        }
        var selection=PageUi.Status();
        var status=PageUi.Status();
        var stack=PageUi.Column();
        stack.Children.Add(PageUi.Hint(
            "اختر سيرفرا واحدا أو عدة سيرفرات عبر المربعات، أو اختر تحديد الكل. "+
            "التفعيل والتعطيل ينفذان على السيرفرات المحددة فقط بعد التأكيد، "+
            "وقد يتوقف اتصال المشتركين عند التعطيل."));
        stack.Children.Add(buttons);
        stack.Children.Add(selection);
        stack.Children.Add(grid);
        stack.Children.Add(status);
        Content=embedded?PageUi.Surface(stack,"#A67A49"):
            PageUi.Wrap("سيرفرات Hotspot",
                "قراءة وتفعيل وتعطيل السيرفرات الفعلية مع التحقق من الحالة لكل سيرفر.",
                PageUi.Surface(stack,"#A67A49"));

        bool busy=false;
        bool loadedOnce=false;
        RouterHotspotServerStatus[] Selected()=>
            grid.SelectedItems.OfType<RouterHotspotServerStatus>().ToArray();
        void UpdateActions()
        {
            var selected=Selected();
            var valid=selected.Length>0&&selected.All(s=>s.Disabled.HasValue);
            enable.IsEnabled=!busy&&valid&&selected.Any(s=>s.Disabled==true);
            disable.IsEnabled=!busy&&valid&&selected.Any(s=>s.Disabled==false);
            clear.IsEnabled=!busy&&selected.Length>0;
            selectAll.IsEnabled=!busy&&grid.Items.Count>0;
            refresh.IsEnabled=!busy;
            selection.Text=$"المحدد: {selected.Length:N0} من {grid.Items.Count:N0}"+
                (selected.Any(s=>s.Disabled is null)
                    ?" | توجد حالة غير معروفة: حدث البيانات أو ألغ تحديد هذا السيرفر."
                    :"");
        }

        async Task Load()
        {
            if(busy)return;
            busy=true;UpdateActions();
            status.Text="يجري قراءة سيرفرات Hotspot الفعلية من RouterOS...";
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(45));
            try
            {
                var servers=await gateway.ReadHotspotServerStatusAsync(timeout.Token);
                if(!IsLoaded)return;
                grid.ItemsSource=servers;
                status.Text=$"تم جلب {servers.Count:N0} سيرفرا من الراوتر | "+
                    DateTime.Now.ToString("HH:mm:ss")+
                    " | اختر المطلوب ثم نفذ التفعيل أو التعطيل.";
            }
            catch(Exception ex)
            {
                grid.ItemsSource=null;
                status.Text="فشلت قراءة السيرفرات؛ ألغيت البيانات القديمة: "+ex.Message;
            }
            finally{busy=false;UpdateActions();}
        }

        async Task Change(bool disabled)
        {
            if(busy)return;
            var chosen=Selected();
            if(chosen.Length==0)
            {
                status.Text="حدد سيرفرا واحدا أو مجموعة من السيرفرات أولا.";
                return;
            }
            if(chosen.Any(x=>x.Disabled is null))
            {
                status.Text="توجد سيرفرات بحالة غير معروفة؛ لا يمكن تعديلها قبل تحديث البيانات.";
                return;
            }
            var changing=chosen.Where(x=>x.Disabled!=disabled).ToArray();
            if(changing.Length==0)
            {
                status.Text="جميع السيرفرات المحددة بالفعل بالحالة المطلوبة.";
                return;
            }
            // Do not impose an arbitrary 100/256-server UI limit; RouterOS
            // inventory and the gateway's bounded batch guard the selection.
            var names=string.Join("، ",changing.Take(5).Select(x=>x.Name));
            if(changing.Length>5)names+="، ...";
            var action=disabled?"تعطيل":"تفعيل";
            if(!PageUi.Confirm(action+" "+changing.Length+" سيرفرا من Hotspot؟\n"+
                "السيرفرات: "+names+"\n"+
                (disabled?"قد ينقطع اتصال المستخدمين على هذه السيرفرات.":"ستتاح السيرفرات المفعلة للمشتركين.")+
                "\nسيتم فحص هوية وحالة كل سيرفر قبل العملية والتحقق بعدها."))
                return;

            // Require explicit additional approval before taking down the
            // last active Hotspot server(s) in the currently loaded inventory.
            var activeCount=grid.Items.OfType<RouterHotspotServerStatus>()
                .Count(x=>x.Disabled==false);
            if(disabled&&activeCount>0&&changing.Length==activeCount &&
                !PageUi.Confirm(
                    "تأكيد إضافي: هذا الاختيار سيعطل جميع سيرفرات Hotspot "+
                    "المفعلة التي ظهرت في القائمة! قد تنقطع خدمة الإنترنت عن جميع المشتركين. "+
                    "هل تريد التنفيذ رغم ذلك؟"))
                return;
            busy=true;UpdateActions();
            status.Text=$"يجري {action} {changing.Length} سيرفرا والتحقق من النتيجة...";
            string resultText;
            try
            {
                var result=await gateway.ChangeHotspotServersStateAsync(chosen,disabled);
                resultText=result.Diagnostic+
                    (result.UncertainOutcome
                        ?" | نتيجة غير مؤكدة: لا تعِد التنفيذ دون فحص الراوتر.":"");
            }
            catch(Exception ex)
            {
                resultText="توقفت العملية: "+ex.Message+
                    " | تحقق من حالة السيرفرات قبل تكرار أي أمر.";
            }
            finally{busy=false;UpdateActions();}

            // Even after a rejected/uncertain write the old selection is stale.
            // A read-only refresh cannot duplicate a destructive mutation.
            grid.ItemsSource=null;
            UpdateActions();
            await Load();
            status.Text=resultText+" | "+status.Text;
        }

        refresh.Click+=async(_,_)=>await Load();
        enable.Click+=async(_,_)=>await Change(false);
        disable.Click+=async(_,_)=>await Change(true);
        selectAll.Click+=(_,_)=>grid.SelectAll();
        clear.Click+=(_,_)=>grid.UnselectAll();
        grid.SelectionChanged+=(_,_)=>UpdateActions();
        Loaded+=async(_,_)=>{if(!loadedOnce){loadedOnce=true;await Load();}};
        UpdateActions();
    }
}
