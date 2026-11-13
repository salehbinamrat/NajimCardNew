using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using NajmCard.Core.Models;

namespace NajmCard.Desktop.Views;

public partial class DashboardView
{
    private bool _maintenanceBusy;

    // Both dashboard shortcuts lead to the authoritative server table.
    // No server is modified from a quick-action pop-up: the operator can
    // select one, several, or all servers and confirm the chosen action there.
    private void RestoreHotspot_Click(object sender,RoutedEventArgs e) =>
        _navigate("servers");

    private void DisableHotspot_Click(object sender,RoutedEventArgs e) =>
        _navigate("servers");

    private async void RebootRouter_Click(object sender,RoutedEventArgs e)
    {
        if(_maintenanceBusy)return;
        if(!PageUi.Confirm("إعادة تشغيل الراوتر ستقطع الإنترنت عن جميع المستخدمين "+
            "مؤقتا. هل تريد المتابعة؟"))return;
        if(MessageBox.Show("تأكيد نهائي: إرسال أمر إعادة تشغيل MikroTik الآن؟",
            "نجم كارد | إعادة التشغيل",MessageBoxButton.YesNo,
            MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
        _maintenanceBusy=true;
        try
        {
            var outcome=await _gateway.RequestRebootAsync();
            MessageBox.Show(outcome.Message,"نجم كارد | إعادة تشغيل الراوتر",
                MessageBoxButton.OK,
                outcome.Accepted?MessageBoxImage.Information:MessageBoxImage.Warning);
        }
        catch(Exception ex)
        {
            MessageBox.Show("فشل طلب إعادة التشغيل: "+ex.Message+
                " | تحقق من الراوتر ولا تعاود الطلب آليا.",
                "نجم كارد | الصيانة",MessageBoxButton.OK,MessageBoxImage.Error);
        }
        finally{_maintenanceBusy=false;}
    }
}
