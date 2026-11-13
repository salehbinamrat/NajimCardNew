using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NajmCard.Core.Services;

namespace NajmCard.Desktop.Views;

/// <summary>Dedicated, read-only view of the complete router-reported package
/// distribution. The fixed-size dashboard itself remains scroll-free.</summary>
public partial class DashboardView
{
    private async void AllPackages_Click(object sender,RoutedEventArgs e)
    {
        // A first click must not silently do nothing while the expensive
        // RouterOS v6 inventory has not yet been loaded.
        if(_lastLifecycle is null || !_lastLifecycle.IsComplete)
        {
            if(_lifecycleReading)
            {
                LifecycleStatus.Text="جارٍ تحميل بيانات الباقات؛ انتظر اكتمال القراءة ثم أعد فتح القائمة.";
                return;
            }
            await RefreshLifecycleAsync(force:true);
            if(_lastLifecycle is null || !_lastLifecycle.IsComplete)
            {
                LifecycleStatus.Text="تعذر عرض الباقات: لم تكتمل قراءة بيانات User Manager v6.";
                return;
            }
        }
        var inventory=_lastLifecycle;
        // Use only the confirmed, full v6 inventory for per-package counts.
        // Assigned users are not necessarily currently online, and Started
        // does not prove a recent purchase or accounting session.
        var groups=UserManagerV6PackageUsage.CountUsed(inventory);
        var grid=UmUi.Table(
            ("الباقة","Name",360),
            ("عدد الكروت المستخدمة","Used",200));
        grid.ItemsSource=groups;
        grid.MinHeight=300;
        grid.MaxHeight=430;
        var close=UmUi.Action("إغلاق",true,105);
        var dialog=new Window
        {
            Title="نجم كارد | جميع الباقات وإحصائية الحسابات",
            Width=770,Height=585,MinWidth=660,MinHeight=495,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Owner=Window.GetWindow(this),
            Background=UmUi.Light,
            FlowDirection=FlowDirection.RightToLeft,
            Content=UmUi.Stack(
                UmUi.Header("أكثر الباقات استخداما",
                    "عدد الكروت التي بدأت استخدامها في كل باقة."),
                UmUi.Panel(grid),
                UmUi.Row(close))
        };
        close.Click+=(_,_)=>dialog.Close();
        dialog.ShowDialog();
    }
}
