using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace NajmCard.Desktop.Views;

/// <summary>Dedicated, read-only view of the complete router-reported package
/// distribution. The fixed-size dashboard itself remains scroll-free.</summary>
public partial class DashboardView
{
    private sealed record PackageUsageDisplay(string Name,int Used);

    private void AllPackages_Click(object sender,RoutedEventArgs e)
    {
        var inventory=_lastLifecycle;
        if(inventory is null||!inventory.IsComplete)return;
        // Use only the confirmed, full v6 inventory for per-package counts.
        // Assigned users are not necessarily currently online, and Started
        // does not prove a recent purchase or accounting session.
        var groups=inventory.Users
            .Where(x=>!string.IsNullOrWhiteSpace(x.Profile))
            .GroupBy(x=>x.Profile,StringComparer.OrdinalIgnoreCase)
            .Select(g=>new PackageUsageDisplay(g.Key,g.Count(x=>x.Started)))
            .OrderByDescending(x=>x.Used)
            .ThenBy(x=>x.Name,StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
