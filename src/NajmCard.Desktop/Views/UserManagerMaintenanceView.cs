using System.Windows;
using System.Windows.Controls;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using NajmCard.Desktop.Services;

namespace NajmCard.Desktop.Views;

/// <summary>
/// RouterOS User Manager maintenance diagnostics and a clearly separate LOCAL
/// NajimCard data backup. Does not claim to back up or restore the router DB.
/// </summary>
public sealed class UserManagerMaintenanceView : UserControl
{
    public UserManagerMaintenanceView(RouterGateway gateway,VoucherStore? store,Action back)
    {
        var content=PageUi.Column();
        var returnButton=PageUi.Button("العودة إلى إدارة User Manager",false);
        returnButton.Click+=(_,_)=>back();
        content.Children.Add(returnButton);
        content.Children.Add(PageUi.Title("النسخ والصيانة"));
        content.Children.Add(PageUi.Hint(
            "تشخيص قاعدة User Manager على المايكروتيك دون تعديلها، "+
            "ونسخة محلية مستقلة من سجلات نجم كارد عند الحاجة."));

        var diagnose=PageUi.Button("فحص قاعدة User Manager");
        var result=PageUi.Status();
        result.Text="اضغط الفحص لمراجعة قاعدة User Manager v6 أو جاهزية RADIUS على v7.";
        content.Children.Add(diagnose);
        content.Children.Add(result);

        var separator=new Separator {Margin=new Thickness(0,12,0,12)};
        content.Children.Add(separator);
        content.Children.Add(PageUi.Label("النسخ الاحتياطي المحلي"));
        content.Children.Add(PageUi.Hint(
            "هذه النسخة تحفظ سجلات نجم كارد المحلية فقط، ولا تحفظ قاعدة User Manager "+
            "على الراوتر ولا تكفي لاستعادة كلمات مرور محمية بـ Windows DPAPI على جهاز آخر."));
        var backup=PageUi.Button("نسخ بيانات نجم كارد المحلية",false);
        backup.IsEnabled=store is not null;
        var backupResult=PageUi.Status();
        content.Children.Add(backup);
        content.Children.Add(backupResult);
        content.Children.Add(PageUi.Label("استعادة قاعدة User Manager"));
        content.Children.Add(PageUi.Hint(
            "لا يمكن تنفيذ استعادة قاعدة User Manager عبر هذا القسم حاليا. "+
            "استعادة الراوتر أو قاعدة المستخدمين تتطلب نسخة موثقة من الراوتر نفسه "+
            "وفحوص توافق قبل أي كتابة؛ لن تُعرض رسالة نجاح وهمية."));

        Content=PageUi.Wrap("النسخ والصيانة",
            "فحص حالة الخدمة ونسخ بيانات التطبيق المحلية بصورة آمنة.",
            PageUi.Surface(content,"#0A8F9A"));

        bool working=false;
        diagnose.Click+=async (_,_) =>
        {
            if(working || !IsLoaded)return;
            working=true;
            diagnose.IsEnabled=backup.IsEnabled=false;
            result.Text="يجري فحص حالة User Manager من الراوتر دون تغيير الإعدادات...";
            try
            {
                var version=await gateway.ReadRouterOsGenerationAsync();
                if(!IsLoaded)return;
                if(version==RouterOsGeneration.V6)
                {
                    var report=await gateway.ReadUserManagerV6DatabaseStatusAsync();
                    if(!IsLoaded)return;
                    result.Text=report.Diagnostic+
                        " | حجم القاعدة: "+report.DbSize+
                        " | المساحة الحرة: "+report.FreeSpace;
                }
                else
                {
                    var report=await gateway.DiagnoseUserManagerV7RadiusAsync();
                    if(!IsLoaded)return;
                    result.Text=report.Message+
                        (report.Issues.Count>0
                            ?" | "+string.Join(" | ",report.Issues):"");
                }
            }
            catch(Exception ex)
            {
                if(IsLoaded)result.Text="تعذر فحص User Manager: "+ex.Message;
            }
            finally
            {
                working=false;
                diagnose.IsEnabled=true;
                backup.IsEnabled=store is not null;
            }
        };
        backup.Click+=(_,_) =>
        {
            if(working || store is null)return;
            if(!PageUi.Confirm(
                "إنشاء نسخة احتياطية محلية لبيانات نجم كارد فقط؟ "+
                "لن تتغير قاعدة User Manager على المايكروتيك."))
                return;
            try
            {
                backupResult.Text="تم إنشاء نسخة البيانات المحلية: "+store.Backup();
            }
            catch(Exception ex){backupResult.Text="فشل إنشاء النسخة المحلية: "+ex.Message;}
        };
    }
}
