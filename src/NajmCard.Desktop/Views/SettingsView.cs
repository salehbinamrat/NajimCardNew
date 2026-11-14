using System.Windows;
using System.Windows.Controls;
using NajmCard.Desktop.Services;

namespace NajmCard.Desktop.Views;

/// <summary>
/// Settings use the same cream cards and burgundy/gold outlines as the rest
/// of Najm Card. Shortcut creation is strictly opt-in and does not touch the
/// user's Startup folder, registry or the original application's directory.
/// </summary>
public class SettingsView : UserControl
{
    public SettingsView(VoucherStore store)
    {
        var root=PageUi.Column();

        var backupPanel=PageUi.Column();
        var backupHeading=PageUi.Label("البيانات والنسخ الاحتياطية");
        backupHeading.FontSize=17;
        backupPanel.Children.Add(backupHeading);
        backupPanel.Children.Add(PageUi.Hint(
            "مسار حفظ بيانات تريم كارد المستقلة:"));
        backupPanel.Children.Add(PageUi.Hint(store.DirectoryPath));
        backupPanel.Children.Add(PageUi.Hint(
            "كلمات مرور الكروت محمية بميزة Windows DPAPI لحساب المستخدم المحلي. "+
            "نقل قاعدة البيانات إلى حساب Windows آخر لا يكفي لاستعادتها."));
        var backup=PageUi.Button("إنشاء نسخة احتياطية الآن");
        var backupStatus=PageUi.Status();
        backupPanel.Children.Add(backup);
        backupPanel.Children.Add(backupStatus);
        root.Children.Add(PageUi.Surface(backupPanel));

        var shortcutPanel=PageUi.Column();
        var shortcutHeading=PageUi.Label("التشغيل واختصار سطح المكتب");
        shortcutHeading.FontSize=17;
        shortcutPanel.Children.Add(shortcutHeading);
        shortcutPanel.Children.Add(PageUi.Hint(
            "يمكنك تشغيل النسخة المحمولة مباشرة من NajmCard.exe الموجود بجانب "+
            "مجلد App، أو إنشاء اختصار لفتح البرنامج من سطح المكتب."));
        shortcutPanel.Children.Add(PageUi.Hint(
            "إنشاء الاختصار اختياري. لن ينسخ ملفات البرنامج أو ينقله؛ "+
            "إذا نقلت مجلد Portable إلى مكان آخر، أنشئ الاختصار من موقعه الجديد."));
        var shortcut=PageUi.Button("إنشاء اختصار على سطح المكتب");
        var shortcutStatus=PageUi.Status();
        shortcutPanel.Children.Add(shortcut);
        shortcutPanel.Children.Add(shortcutStatus);
        root.Children.Add(PageUi.Surface(shortcutPanel));

        Content=PageUi.Wrap("الإعدادات والنسخ الاحتياطي",
            "إعدادات تريم كارد مستقلة ولا تشارك برنامج حضرموت أو نجم القديم قواعد بياناته.",
            root);

        backup.Click+=(_,_)=>
        {
            try
            {
                backupStatus.Text="تم إنشاء النسخة: "+store.Backup();
            }
            catch(Exception ex)
            {
                backupStatus.Text="فشل النسخ: "+ex.Message;
            }
        };
        shortcut.Click+=(_,_)=>
        {
            try
            {
                string created=DesktopShortcutService.CreateOrUpdateShortcut();
                shortcutStatus.Text="تم إنشاء أو تحديث اختصار تريم كارد على سطح المكتب: "+
                    created;
            }
            catch(Exception ex)
            {
                shortcutStatus.Text="تعذر إنشاء اختصار سطح المكتب: "+ex.Message;
            }
        };
    }
}
