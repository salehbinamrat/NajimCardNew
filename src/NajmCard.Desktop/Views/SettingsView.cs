using System.Windows;
using System.Windows.Controls;
using NajmCard.Desktop.Services;
using Microsoft.Win32;

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
        var restore=PageUi.Button("استعادة نسخة محلية",false);
        var backupStatus=PageUi.Status();
        backupPanel.Children.Add(backup);
        backupPanel.Children.Add(restore);
        backupPanel.Children.Add(PageUi.Hint("النسخة الكاملة .najmbackup تشمل القاعدة وصور القوالب. الاستعادة تحفظ نسخة رجوع أولاً ثم تُغلق البرنامج. ملفات الترخيص المستقلة لا تتغير."));
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

        backup.Click+=async(_,_)=>
        {
            try
            {
                backup.IsEnabled=restore.IsEnabled=false;
                backupStatus.Text="جارٍ نسخ القاعدة وصور القوالب...";
                var file=await Task.Run(()=>store.BackupArchive());
                backupStatus.Text="تم إنشاء النسخة الكاملة: "+file;
            }
            catch(Exception ex)
            {
                backupStatus.Text="فشل النسخ: "+ex.Message;
            }
            finally{backup.IsEnabled=restore.IsEnabled=true;}
        };
        restore.Click+=async(_,_)=>
        {
            var picker=new OpenFileDialog{Filter="نسخ تريم كارد (*.najmbackup;*.db)|*.najmbackup;*.db",CheckFileExists=true};
            if(picker.ShowDialog()!=true)return;
            if(!PageUi.Confirm("ستستبدل هذه العملية البيانات المحلية بالنسخة المختارة، وتحفظ نسخة رجوع كاملة أولاً. أغلق نسخ البرنامج الأخرى وانتظر اكتمال الإرسال. سيغلق البرنامج بعد النجاح؛ لا يُعدّل أي راوتر. هل تؤكد الاستعادة؟"))return;
            try
            {
                backup.IsEnabled=restore.IsEnabled=false;
                backupStatus.Text="جارٍ فحص النسخة والصور وإنشاء نسخة الرجوع...";
                var result=await Task.Run(()=>store.RestoreBackup(picker.FileName));
                MessageBox.Show("تمت استعادة "+result.Cards+" كرت و"+result.Images+" صورة. نسخة الرجوع:\n"+
                    result.RecoveryBackupPath+"\nأعد تشغيل البرنامج لقراءة البيانات المستعادة.","استعادة مكتملة",MessageBoxButton.OK,MessageBoxImage.Information);
                Application.Current.Shutdown();
            }
            catch(Exception ex){backupStatus.Text="لم تكتمل الاستعادة: "+ex.Message;}
            finally{backup.IsEnabled=restore.IsEnabled=true;}
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
