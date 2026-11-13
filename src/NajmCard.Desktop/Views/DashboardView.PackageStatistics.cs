using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace NajmCard.Desktop.Views;

/// <summary>Dedicated, read-only view of the complete router-reported package
/// distribution. The fixed-size dashboard itself remains scroll-free.</summary>
public partial class DashboardView
{
    private sealed record PackageUsageDisplay(string Name,int Used,int Total,
        string Sessions);

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
            .Select(g=>new PackageUsageDisplay(g.Key,
                g.Count(x=>x.Started),g.Count(),
                g.All(x=>x.ActiveSessionsVerified)
                    ?g.Sum(x=>(long)x.ActiveSessions).ToString("N0")
                    :"—"))
            .OrderByDescending(x=>x.Total)
            .ThenBy(x=>x.Name,StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var grid=UmUi.Table(
            ("اسم الباقة","Name",260),
            ("الكروت المستخدمة","Used",160),
            ("إجمالي الكروت المرتبطة","Total",170),
            ("الجلسات الحالية (إن توفرت)","Sessions",190));
        grid.ItemsSource=groups;
        grid.MinHeight=300;
        grid.MaxHeight=430;
        var summary=UmUi.Label(
            $"عدد الباقات: {groups.Length:N0} | "+
            $"الحسابات المصنفة ضمنها: {groups.Sum(x=>x.Total):N0} | "+
            "المستخدمة تعني كروتا بدأت استخدامها حسب User Manager، "+
            "والإجمالي حسابات الباقة. الجلسات الحالية مأخوذة من active-sessions "+
            "عندما يوفر RouterOS هذا الحقل؛ وإلا تظهر (—) بدلا من الصفر.",12);
        summary.TextWrapping=TextWrapping.Wrap;
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
                UmUi.Header("جميع باقات User Manager",
                    "بيانات مباشرة من آخر قراءة مكتملة للحسابات على الراوتر."),
                UmUi.Panel(UmUi.Stack(summary,grid)),
                UmUi.Row(close))
        };
        close.Click+=(_,_)=>dialog.Close();
        dialog.ShowDialog();
    }
}
